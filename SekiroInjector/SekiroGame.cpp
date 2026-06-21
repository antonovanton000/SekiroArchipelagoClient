// SekiroGame.cpp
#include "pch.h"
#include "SekiroGame.h"
#include "SekiroTypes.h"
#include "Utils.h"
#include "Log.h"
#include "Overlay.h"       
#include "Core.h"       
#include <vector>
#include <mutex>
#include <Psapi.h>
#include "InGameMessaging.h"
#include "PipeConnection.h"
#include "EndingDetector.h"
#include "ItemHooks.h"
#include "MinHook.h"
#include <deque>

extern PipeConnection g_Pipe;

// Pointer to storage location where MapItemMan* is held (sekiro.exe + 0x3D6CDC0)
static uintptr_t g_MapItemManStorageAddr = 0;

// Cached MapItemMan* once resolved
static uintptr_t g_MapItemMan = 0;

// Log guard to avoid spam
static bool g_MapItemManLoggedWaiting = false;
static bool g_MapItemManLoggedResolved = false;
static bool g_GameDataManLoggedResolved = false;

// Pointer to WorldChrMan storage 
static uintptr_t g_WCMStorageAddr = 0;

// Pointer to PlayerGameData storage 
static uintptr_t g_PGDStorageAddr = 0;

static bool g_Initialized = false;
static bool g_WorldLoaded = false;
static bool g_WorldJustLoaded = false;
static bool g_WorldJustUnloaded = false;

static bool g_OverlayStarted = false;
thread_local bool g_InOurGrant = false;

static bool g_DeathFromDeathlink = false;

static std::mutex g_PickupMutex;
static std::vector<PickupEvent> g_PickupQueue;

struct QueuedApGrant
{
	uint32_t eventId;
	uint32_t goodsId;
	uint32_t quantity;
	uint32_t deliveryFlagId;
	uint32_t grantRequestId;
};

static std::mutex g_ApGrantQueueMutex;
static std::deque<QueuedApGrant> g_ApGrantQueue;
static ULONGLONG g_LastApGrantMs = 0;
static bool g_ApGrantWaitLogged = false;
static constexpr ULONGLONG AP_GRANT_INTERVAL_MS = 100;

static uintptr_t g_GameDataManStorage = 0;
static uintptr_t g_GameDataMan = 0;

enum class PlayerLifeState
{
	Unknown,
	Alive,
	Dead
};

static PlayerLifeState g_LifeState = PlayerLifeState::Unknown;

// ---------------- EventFlag support (from old FogWall code) ----------------
// RVAs relative to sekiro.exe base
static const uint64_t GETFLAG_RVA = 0x006C3E60;
static const uint64_t SETFLAG_RVA = 0x006AAAB0;
static const uint64_t MGR_PTR_RVA = 0x03D55FE8;
static const uint64_t DEBUG_FLAGS_RVA_1_06 = 0x03D7A369;
static const int64_t PLAYER_EXTERMINATE_OFFSET = -2;
static const int64_t ALL_NO_UPDATE_AI_OFFSET = 13;
static const uint8_t DEBUG_FLAG_ENABLED_MASK = 0x01;

using TSetEventFlag = void(__fastcall*)(void* mgr, uint32_t flagId, uint8_t value);
TSetEventFlag g_SetEventFlag = nullptr;

using TGetEventFlag = uint8_t(__fastcall*)(void* mgr, uint32_t flagId);
TGetEventFlag g_GetEventFlag;

static void* g_EventFlagMgr = nullptr;
static bool  g_EventFlagInitialized = false;

MapItemMan_GrantItem_t g_MapItemMan_GrantItem = nullptr;

using InventoryChange_t =
int(__fastcall*)(uintptr_t inventoryMgr,
	uint32_t slotIndex,
	uint32_t amount,
	uint8_t  flags);

static InventoryChange_t g_InventoryChange = nullptr;

uintptr_t FindWorldChrManPtr()
{
	HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
	if (!hMod) return 0;

	uintptr_t base = reinterpret_cast<uintptr_t>(hMod);
	MODULEINFO info{};
	GetModuleInformation(GetCurrentProcess(), hMod, &info, sizeof(info));
	uintptr_t size = (uintptr_t)info.SizeOfImage;

	const BYTE pattern[] = { 0x48, 0x8B, 0x35, 0x00, 0x00, 0x00, 0x00, 0x44, 0x0F, 0x28, 0x18 };
	const char mask[] = "xxx????xxxx";

	for (uintptr_t i = base; i < base + size - sizeof(pattern); i++)
	{
		bool found = true;
		for (size_t j = 0; j < sizeof(pattern); j++)
		{
			if (mask[j] != '?' && ((BYTE*)i)[j] != pattern[j])
			{
				found = false;
				break;
			}
		}
		if (found)
		{
			int rel = *(int*)(i + 3);
			uintptr_t worldChrManAddr = i + 7 + rel;
			Logf("[FindWorldChrManPtr] Found at 0x%llX (ptr=0x%llX)", i, worldChrManAddr);
			return worldChrManAddr;
		}
	}

	Log("[FindWorldChrManPtr] Pattern not found");
	return 0;
}

uintptr_t FindPlayerGameDataPtr()
{
	HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
	if (!hMod) return 0;

	uintptr_t base = reinterpret_cast<uintptr_t>(hMod);
	MODULEINFO info{};
	GetModuleInformation(GetCurrentProcess(), hMod, &info, sizeof(info));
	uintptr_t size = (uintptr_t)info.SizeOfImage;

	const BYTE pattern[] = { 0x48, 0x8b, 0x0d, 0x00, 0x00, 0x00, 0x00, 0x48, 0x8b, 0x41, 0x20, 0xc6, 0x04, 0x02, 0x00 };
	const char mask[] = "xxx????xxxxxxxx";

	for (uintptr_t i = base; i < base + size - sizeof(pattern); i++)
	{
		bool found = true;
		for (size_t j = 0; j < sizeof(pattern); j++)
		{
			if (mask[j] != '?' && ((BYTE*)i)[j] != pattern[j])
			{
				found = false;
				break;
			}
		}
		if (found)
		{
			int rel = *(int*)(i + 3);
			uintptr_t gameDataAddr = i + 7 + rel;
			Logf("[FindPlayerGameDataPtr] Found at 0x%llX (ptr=0x%llX)", i, gameDataAddr);
			return gameDataAddr;
		}
	}

	Log("[FindPlayerGameDataPtr] Pattern not found");
	return 0;
}


// ---------------------------------------------------------
// World loaded check (same logic you used before)
// ---------------------------------------------------------


int GetPlayerHp()
{
	int hp = -1;
	if (!g_WCMStorageAddr)
	{
		g_WCMStorageAddr = FindWorldChrManPtr();
		if (!g_WCMStorageAddr)
		{
			return -1;
		}
		Logf("[SekiroGame] WorldChrMan storage = 0x%llX", g_WCMStorageAddr);
	}

	// 2) Read WorldChrMan*
	uintptr_t worldChrMan = 0;
	if (!SafeReadPtr(g_WCMStorageAddr, worldChrMan) || !worldChrMan)
		return -1;

	// 3) Follow the same pointer chain you already used
	uintptr_t p1 = 0, p2 = 0, p3 = 0;
	if (!SafeReadPtr(worldChrMan + 0x88, p1) || !p1) return -1;
	if (!SafeReadPtr(p1 + 0x1FF8, p2) || !p2) return -1;
	if (!SafeReadPtr(p2 + 0x18, p3) || !p3) return -1;

	if (!SafeReadInt(p3 + 0x130, hp)) return -1;
	return hp;
}

bool IsWorldLoaded()
{
	if (!g_WCMStorageAddr) {
		g_WCMStorageAddr = FindWorldChrManPtr();
		if (!g_WCMStorageAddr) {
			static bool loggedMissingStorage = false;
			if (!loggedMissingStorage) {
				Log("[WorldLoaded] WorldChrMan storage pattern not found");
				loggedMissingStorage = true;
			}
			return false;
		}
	}

	uintptr_t worldChrMan = 0;
	if (!SafeReadPtr(g_WCMStorageAddr, worldChrMan) || !worldChrMan) {
		static bool loggedMissingWorldChrMan = false;
		if (!loggedMissingWorldChrMan) {
			Logf("[WorldLoaded] WorldChrMan pointer is not ready. storage=0x%llX", g_WCMStorageAddr);
			loggedMissingWorldChrMan = true;
		}
		return false;
	}

	uintptr_t p1 = 0, p2 = 0, p3 = 0;
	if (!SafeReadPtr(worldChrMan + 0x48, p1) || !p1) {
		static bool loggedMissingCoordP1 = false;
		if (!loggedMissingCoordP1) {
			Logf("[WorldLoaded] Coordinate chain p1 failed. worldChrMan=0x%llX", worldChrMan);
			loggedMissingCoordP1 = true;
		}
		int hp = GetPlayerHp();
		if (hp >= 0) {
			Logf("[WorldLoaded] HP fallback succeeded. hp=%d", hp);
			return true;
		}
		return false;
	}
	if (!SafeReadPtr(p1 + 0x28, p2) || !p2) {
		static bool loggedMissingCoordP2 = false;
		if (!loggedMissingCoordP2) {
			Logf("[WorldLoaded] Coordinate chain p2 failed. worldChrMan=0x%llX p1=0x%llX", worldChrMan, p1);
			loggedMissingCoordP2 = true;
		}
		int hp = GetPlayerHp();
		if (hp >= 0) {
			Logf("[WorldLoaded] HP fallback succeeded. hp=%d", hp);
			return true;
		}
		return false;
	}

	float plX = 0;
	float plY = 0;
	if (!SafeReadFloat(p2 + 0x80, plX)) {
		static bool loggedMissingCoordX = false;
		if (!loggedMissingCoordX) {
			Logf("[WorldLoaded] Coordinate X read failed. p2=0x%llX", p2);
			loggedMissingCoordX = true;
		}
		int hp = GetPlayerHp();
		if (hp >= 0) {
			Logf("[WorldLoaded] HP fallback succeeded. hp=%d", hp);
			return true;
		}
		return false;
	}
	if (!SafeReadFloat(p2 + 0x84, plY)) {
		static bool loggedMissingCoordY = false;
		if (!loggedMissingCoordY) {
			Logf("[WorldLoaded] Coordinate Y read failed. p2=0x%llX x=%f", p2, plX);
			loggedMissingCoordY = true;
		}
		int hp = GetPlayerHp();
		if (hp >= 0) {
			Logf("[WorldLoaded] HP fallback succeeded. hp=%d", hp);
			return true;
		}
		return false;
	}
	if (plX != 0 && plY != 0) {
		return true;
	}

	static bool loggedZeroCoords = false;
	if (!loggedZeroCoords) {
		Logf("[WorldLoaded] Coordinate chain read zero position. p2=0x%llX x=%f y=%f", p2, plX, plY);
		loggedZeroCoords = true;
	}

	int hp = GetPlayerHp();
	if (hp >= 0) {
		Logf("[WorldLoaded] HP fallback succeeded after zero coords. hp=%d", hp);
		return true;
	}

	return false;

}

bool GetPlayerDeathFlag(int& outDeathFlag)
{
	outDeathFlag = 0;

	if (!g_PGDStorageAddr)
	{
		g_PGDStorageAddr = FindPlayerGameDataPtr();
		if (!g_PGDStorageAddr)
			return false;

		Logf("[SekiroGame] PlayerGameData storage = 0x%llX", g_PGDStorageAddr);
	}

	// 2) Read WorldChrMan*
	uintptr_t playerGameData = 0;
	if (!SafeReadPtr(g_PGDStorageAddr, playerGameData) || !playerGameData)
		return -1;

	// 3) Follow the same pointer chain you already used
	uintptr_t p1 = 0;
	if (!SafeReadPtr(playerGameData + 0x8, p1) || !p1) return -1;
	if (!SafeReadInt(p1 + 0x178, outDeathFlag)) return -1;
	return true;
}

void UpdatePlayerDeathState()
{
	// If world is not loaded (menu/loading), reset state so we don't fire false positives
	if (!IsWorldLoaded())
	{	
		g_LifeState = PlayerLifeState::Unknown;
		if (g_DeathFromDeathlink)
		{
			Log("[Player] Reset DeathLink death suppression because world is not loaded");
			g_DeathFromDeathlink = false;
		}
		return;
	}


	bool isDeath;

	if (g_IsFullDeathDetection)
	{
		// Read "real death" flag from PlayerGameData (p1 + 0x160)
		int deathFlag = 0;
		if (!GetPlayerDeathFlag(deathFlag)) // implement using your PGD chain + SafeReadInt(p1 + 0x160, deathFlag)
			return;

		isDeath = (deathFlag != 0);
	}
	else
	{
		int playerHp = GetPlayerHp();
		isDeath = (playerHp == 0);
	}

	if (g_DeathFromDeathlink && !isDeath)
	{
		int playerHp = GetPlayerHp();
		if (playerHp > 0)
		{
			Log("[Player] Reset DeathLink death suppression because player is alive again");
			g_DeathFromDeathlink = false;
		}
	}
	
	PlayerLifeState lifeState = isDeath ? PlayerLifeState::Dead : PlayerLifeState::Alive;

	// Fire only on Alive -> Dead transition on DeathFlag
	if (g_LifeState == PlayerLifeState::Alive &&
		lifeState == PlayerLifeState::Dead)
	{
		Logf("[Player] Death detected");
		Overlay_AddLog("[Player] Death detected");
		if (!g_DeathFromDeathlink)
		{
			g_Pipe.SendJson("{ \"type\":\"death\", \"status\": true }");
		}				
		g_DeathFromDeathlink = false;			
		
	}
	else if (lifeState == PlayerLifeState::Dead && g_DeathFromDeathlink)
	{
		Log("[Player] Suppressed DeathLink death while already dead");
		g_DeathFromDeathlink = false;
	}
	g_LifeState = lifeState;
}

void SekiroGame_KillPlayer()
{
	// 1) Resolve WorldChrMan storage
	if (!g_WCMStorageAddr)
	{
		g_WCMStorageAddr = FindWorldChrManPtr();
		if (!g_WCMStorageAddr)
			return;

		Logf("[SekiroGame] WorldChrMan storage = 0x%llX", g_WCMStorageAddr);
	}

	// 2) Read WorldChrMan*
	uintptr_t worldChrMan = 0;
	if (!SafeReadPtr(g_WCMStorageAddr, worldChrMan) || !worldChrMan)
		return;

	// 3) Pointer chain (same as IsWorldLoaded)
	uintptr_t p1 = 0, p2 = 0, p3 = 0;
	if (!SafeReadPtr(worldChrMan + 0x88, p1) || !p1) return;
	if (!SafeReadPtr(p1 + 0x1FF8, p2) || !p2) return;
	if (!SafeReadPtr(p2 + 0x18, p3) || !p3) return;

	// 4) Write HP = 0
	if (!SafeWriteInt(p3 + 0x130, 0))
		return;

	g_DeathFromDeathlink = true;
	Logf("[SekiroGame] KillPlayer: HP set to 0");
}

bool SekiroGame_SetEnemyAiDisabled(bool disabled)
{
	HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
	if (!hMod)
	{
		Log("[DebugFlags] sekiro.exe not loaded");
		return false;
	}

	uintptr_t addr = reinterpret_cast<uintptr_t>(hMod)
		+ DEBUG_FLAGS_RVA_1_06
		+ ALL_NO_UPDATE_AI_OFFSET;

	uint8_t flags = 0;
	if (!SafeReadByte(addr, flags))
	{
		Logf("[DebugFlags] Failed to read all_no_update_ai byte at 0x%llX", addr);
		return false;
	}

	uint8_t newFlags = disabled
		? static_cast<uint8_t>(flags | DEBUG_FLAG_ENABLED_MASK)
		: static_cast<uint8_t>(flags & ~DEBUG_FLAG_ENABLED_MASK);

	if (!SafeWriteByte(addr, newFlags))
	{
		Logf("[DebugFlags] Failed to write all_no_update_ai byte at 0x%llX", addr);
		return false;
	}

	Logf("[DebugFlags] all_no_update_ai: %s (0x%02X -> 0x%02X)",
		disabled ? "true" : "false",
		flags,
		newFlags);
	return true;
}

bool SekiroGame_SetOneHitKillEnabled(bool enabled)
{
	HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
	if (!hMod)
	{
		Log("[DebugFlags] sekiro.exe not loaded");
		return false;
	}

	uintptr_t addr = reinterpret_cast<uintptr_t>(hMod)
		+ DEBUG_FLAGS_RVA_1_06
		+ PLAYER_EXTERMINATE_OFFSET;

	uint8_t flags = 0;
	if (!SafeReadByte(addr, flags))
	{
		Logf("[DebugFlags] Failed to read player_exterminate byte at 0x%llX", addr);
		return false;
	}

	uint8_t newFlags = enabled
		? static_cast<uint8_t>(flags | DEBUG_FLAG_ENABLED_MASK)
		: static_cast<uint8_t>(flags & ~DEBUG_FLAG_ENABLED_MASK);

	if (!SafeWriteByte(addr, newFlags))
	{
		Logf("[DebugFlags] Failed to write player_exterminate byte at 0x%llX", addr);
		return false;
	}

	Logf("[DebugFlags] player_exterminate: %s (0x%02X -> 0x%02X)",
		enabled ? "true" : "false",
		flags,
		newFlags);
	return true;
}

// ---------------------------------------------------------
// Resolve MapItemMan* from its global storage
// ---------------------------------------------------------
bool ResolveMapItemMan(uintptr_t& outMapItemMan)
{
	if (!g_MapItemManStorageAddr)
	{
		HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
		if (!hMod)
			return false;

		g_MapItemManStorageAddr =
			reinterpret_cast<uintptr_t>(hMod) + 0x3D6CDC0;

		Logf("[MapItemMan] Storage resolved at 0x%llX", g_MapItemManStorageAddr);
	}

	if (!SafeReadPtr(g_MapItemManStorageAddr, outMapItemMan))
		return false;

	if (!outMapItemMan)
		return false;

	return true;
}

// ---------------------------------------------------------
// Lazy init for MapItemMan::GrantItem pointer
// ---------------------------------------------------------
bool EnsureGrantItemResolved()
{
	if (!g_MapItemMan_GrantItem)
	{
		HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
		if (!hMod) return false;

		auto base = reinterpret_cast<uintptr_t>(hMod);
		g_MapItemMan_GrantItem = reinterpret_cast<MapItemMan_GrantItem_t>(base + 0x91C970);

		Logf("[MapItemMan] GrantItem = 0x%llX", (uintptr_t)g_MapItemMan_GrantItem);
	}
	return g_MapItemMan_GrantItem != nullptr;
}

// For now we assume all items we give are "goods".
static uint32_t EncodeGoodsId(uint32_t goodsId)
{
	// Category 4 (Goods) in high 4 bits -> 0x40000000 | goodsId
	return (4u << 28) | goodsId;
}

// ---------------- EventFlag helpers ----------------

static bool InitEventFlagSystem()
{
	if (g_EventFlagInitialized)
	{
		// Already initialized: make sure all required pointers are valid.
		return (g_SetEventFlag != nullptr &&
			g_GetEventFlag != nullptr &&
			g_EventFlagMgr != nullptr);
	}

	HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
	if (!hBase)
	{
		Log("[EventFlag] sekiro.exe not loaded");
		g_EventFlagInitialized = false;
		return false;
	}

	auto base = reinterpret_cast<uint8_t*>(hBase);

	// Resolve manager pointer (EventFlagMgr*)
	void** pMgr = reinterpret_cast<void**>(base + MGR_PTR_RVA);

	void* mgr = nullptr;
	__try
	{
		mgr = *pMgr;
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		Log("[EventFlag] Exception while reading EventFlagMgr pointer");
		mgr = nullptr;
	}

	if (!mgr)
	{
		Log("[EventFlag] EventFlagMgr is null");
		g_EventFlagInitialized = false;
		return false;
	}

	// Resolve SetEventFlag function
	auto setFlag = reinterpret_cast<TSetEventFlag>(base + SETFLAG_RVA);
	if (!setFlag)
	{
		Log("[EventFlag] Failed to resolve SetEventFlag RVA");
		g_EventFlagInitialized = false;
		return false;
	}

	// Resolve GetEventFlag function
	auto getFlag = reinterpret_cast<TGetEventFlag>(base + GETFLAG_RVA);
	if (!getFlag)
	{
		Log("[EventFlag] Failed to resolve GetEventFlag RVA");
		g_EventFlagInitialized = false;
		return false;
	}

	g_EventFlagMgr = mgr;
	g_SetEventFlag = setFlag;
	g_GetEventFlag = getFlag;
	g_EventFlagInitialized = true;

	Logf("[EventFlag] Init ok: mgr=%p set=%p get=%p",
		g_EventFlagMgr, g_SetEventFlag, g_GetEventFlag);

	return true;
}

bool SetEventFlagSafe(uint32_t flagId, bool value)
{
	if (!g_SetEventFlag || !g_EventFlagMgr)
	{
		if (!InitEventFlagSystem())
		{
			Logf("[EventFlag] Not initialized, cannot set flag %u", flagId);
			return false;
		}
	}

	uint8_t val = value ? 1u : 0u;

	__try
	{
		g_SetEventFlag(g_EventFlagMgr, flagId, val);
		Logf("[EventFlag] Set flag %u = %u", flagId, (unsigned)val);
		return true;
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		Logf("[EventFlag] Exception in SetEventFlag(%u, %u), skipping",
			flagId, (unsigned)val);
		return false;
	}
}

bool GetEventFlagSafe(uint32_t flagId, bool& outValue)
{
	outValue = false;

	if (!g_GetEventFlag || !g_EventFlagMgr)
	{
		if (!InitEventFlagSystem())
		{
			Logf("[EventFlag] Not initialized, cannot read flag %u", flagId);
			return false;
		}
	}

	__try
	{
		uint8_t val = g_GetEventFlag(g_EventFlagMgr, flagId);
		outValue = (val != 0);		
		return true;
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		Logf("[EventFlag] Exception in GetEventFlag(%u), skipping", flagId);
		outValue = false;
		return false;
	}
}

bool SekiroGame_Initialize()
{
	Logf("[SekiroGame] Initialize");
	g_Initialized = true;
	Sleep(10000);
	InitEventFlagSystem();
	InitInGameMessaging();
	return true;
}


// ---------------------------------------------------------
// Main per-tick game update called from Core
// ---------------------------------------------------------
void SekiroGame_Update()
{
	if (!g_Initialized)
		return;

	// -----------------------------------------------------
	// 1) Detect world loaded / unloaded transitions
	// -----------------------------------------------------
	bool worldNowLoaded = IsWorldLoaded();

	g_WorldJustLoaded = (!g_WorldLoaded && worldNowLoaded);
	g_WorldJustUnloaded = (g_WorldLoaded && !worldNowLoaded);
	g_WorldLoaded = worldNowLoaded;

	if (g_WorldJustUnloaded)
	{
		Log("[SekiroGame] World unloaded");
		Overlay_AddLog("World unloaded");
		g_Pipe.SendJson("{ \"type\":\"world\", \"status\": false }");

		g_MapItemMan = 0;
		g_MapItemManLoggedResolved = false;
		g_MapItemManLoggedWaiting = false;
		return;
	}

	if (!g_WorldLoaded)
	{
		// Still in menus / loading
		return;
	}

	if (g_WorldJustLoaded)
	{
		Log("[SekiroGame] World loaded detected");
		Overlay_AddLog("World loaded");
		g_Pipe.SendJson("{ \"type\":\"world\", \"status\": true }");
	}

	UpdatePlayerDeathState();

	EndingDetector_Update();

	SekiroEndingType ending;
	if (EndingDetector_JustFinished(ending))
	{		
		switch (ending)
		{
		case SekiroEndingType::Shura:
			Logf("[Ending] Run finished with Shura ending");
			Overlay_AddLog("[Ending] Run finished with Shura ending");
			g_Pipe.SendJson("{ \"type\":\"ending\", \"status\": \"shura\" }");
			break;

		case SekiroEndingType::ImmortalSeveranceLike:
			Logf("[Ending] Run finished with long ending (ImmortalSeverance-like)");
			Overlay_AddLog("[Ending] Run finished with long ending (ImmortalSeverance-like)");
			g_Pipe.SendJson("{ \"type\":\"ending\", \"status\": \"immortal\" }");
			break;

		default:
			break;
		}
	}

	// -----------------------------------------------------
	// 2) Resolve MapItemMan once world is loaded
	// -----------------------------------------------------
	if (!g_MapItemMan)
	{
		uintptr_t tmp = 0;
		if (ResolveMapItemMan(tmp))
		{
			g_MapItemMan = tmp;

			if (!g_MapItemManLoggedResolved)
			{
				Logf("[MapItemMan] Instance = 0x%llX", g_MapItemMan);
				g_MapItemManLoggedResolved = true;
				EnsureGrantItemResolved();
			}
		}
		else
		{
			if (!g_MapItemManLoggedWaiting)
			{
				Log("[MapItemMan] Waiting for MapItemMan...");
				g_MapItemManLoggedWaiting = true;
			}
		}

		// Without MapItemMan we cannot grant items
		return;
	}
}


// ---------------------------------------------------------
// Grant a single item to the player via MapItemMan::GrantItem
// ---------------------------------------------------------
bool SekiroGame_GrantItem(const PendingApItem& item)
{
	EnsureGrantItemResolved();

	if (!g_MapItemMan || !g_MapItemMan_GrantItem)
		return false;

	/*const uint32_t rawGoodsId = item.itemId;
	const uint32_t encodedId = EncodeGoodsId(rawGoodsId);*/

	ItemBufferEntry entry{};
	entry.unk0 = 1;          // AddItem
	entry.itemId = item.itemId;  
	entry.quantity = item.quantity;
	entry.unk3 = -1;         // 0xFFFFFFFF
	entry.unk4 = 3;          // inv type

	Logf(
		"[GrantItem] Giving goodsId=%u quantity=%u",		
		entry.itemId,
		entry.quantity
	);

	__try
	{
		g_InOurGrant = true;
		g_MapItemMan_GrantItem(g_MapItemMan, &entry, 0, 0);
	}
	__except (EXCEPTION_EXECUTE_HANDLER)
	{
		Log("[GrantItem] Exception while calling GrantItem");
		g_InOurGrant = false;
		return false;
	}
	g_InOurGrant = false;
	return true;
}


// ---------------------------------------------------------
// Grant item + set event flag first (EventId, GoodId, Count)
// ---------------------------------------------------------
bool SekiroGame_GrantItemWithEvent(uint32_t eventId, uint32_t goodsId, uint32_t count)
{
	if (eventId != 0)
	{
		bool alreadyOwned = false;
		if (GetEventFlagSafe(eventId, alreadyOwned) && alreadyOwned)
		{
			Logf("[GrantItemEF] Skipping goods=%u x%u because event flag %u is already set",
				goodsId, count, eventId);
			return true;
		}
	}

	PendingApItem tmp{};
	tmp.itemId = goodsId;
	tmp.quantity = count;

	Logf("[GrantItemEF] Granting goods=%u x%u (eventId=%u)", goodsId, count, eventId);

	bool granted = SekiroGame_GrantItem(tmp);

	// Do not force-set the permanent item flag here. Sekiro updates these flags as
	// part of its own GrantItem flow, and forcing them externally can corrupt
	// one-off goods inventory state.
	return granted;
}

void SekiroGame_QueueGrantItem(uint32_t eventId, uint32_t goodsId, uint32_t count, uint32_t deliveryFlagId, uint32_t grantRequestId)
{
	{
		std::lock_guard<std::mutex> lock(g_ApGrantQueueMutex);
		g_ApGrantQueue.push_back({ eventId, goodsId, count, deliveryFlagId, grantRequestId });
	}

	Logf("[GrantQueue] queued goods=%u x%u eventId=%u deliveryFlagId=%u grantRequestId=%u", goodsId, count, eventId, deliveryFlagId, grantRequestId);
}

void SekiroGame_ProcessPendingGrants()
{
	if (!IsWorldLoaded())
		return;

	const ULONGLONG now = GetTickCount64();
	if (g_LastApGrantMs != 0 && now - g_LastApGrantMs < AP_GRANT_INTERVAL_MS)
		return;

	QueuedApGrant grant{};
	{
		std::lock_guard<std::mutex> lock(g_ApGrantQueueMutex);
		if (g_ApGrantQueue.empty())
			return;
		grant = g_ApGrantQueue.front();
	}

	if (!ItemHooks_TryBeginApGrant())
	{
		if (!g_ApGrantWaitLogged)
		{
			Log("[GrantQueue] waiting for gameplay item operation quiet period");
			g_ApGrantWaitLogged = true;
		}
		return;
	}

	{
		std::lock_guard<std::mutex> lock(g_ApGrantQueueMutex);
		if (g_ApGrantQueue.empty())
		{
			ItemHooks_EndApGrant();
			return;
		}

		grant = g_ApGrantQueue.front();
		g_ApGrantQueue.pop_front();
	}

	Logf("[GrantQueue] dispatch goods=%u x%u eventId=%u deliveryFlagId=%u grantRequestId=%u", grant.goodsId, grant.quantity, grant.eventId, grant.deliveryFlagId, grant.grantRequestId);
	g_ApGrantWaitLogged = false;

	bool delivered = false;
	if (grant.eventId != 0)
	{
		delivered = SekiroGame_GrantItemWithEvent(grant.eventId, grant.goodsId, grant.quantity);
	}
	else
	{
		PendingApItem item{};
		item.itemId = grant.goodsId;
		item.quantity = grant.quantity;
		delivered = SekiroGame_GrantItem(item);
	}

	if (grant.deliveryFlagId != 0)
	{
		if (delivered)
		{
			SetEventFlagSafe(grant.deliveryFlagId, true);
		}
	}

	if (grant.deliveryFlagId != 0 || grant.grantRequestId != 0)
	{
		char response[192];
		sprintf_s(response,
			"{ \"type\":\"grant_item_ack\", \"delivery_flag_id\":%u, \"grant_request_id\":%u, \"delivered\":%s }",
			grant.deliveryFlagId,
			grant.grantRequestId,
			delivered ? "true" : "false");
		g_Pipe.SendJson(std::string(response));
	}

	g_LastApGrantMs = GetTickCount64();
	ItemHooks_EndApGrant();
}
