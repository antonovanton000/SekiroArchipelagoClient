// SekiroGame.cpp
#include "pch.h"
#include "SekiroGame.h"
#include "SekiroTypes.h"
#include "Utils.h"
#include "Log.h"
#include "Overlay.h"       
#include "OverlayState.h"  
#include <vector>
#include <mutex>
#include <Psapi.h>
#include "InGameMessaging.h"
#include "PipeConnection.h"

extern PipeConnection g_Pipe;

// Pointer to storage location where MapItemMan* is held (sekiro.exe + 0x3D6CDC0)
static uintptr_t g_MapItemManStorageAddr = 0;

// Cached MapItemMan* once resolved
static uintptr_t g_MapItemMan = 0;

// Log guard to avoid spam
static bool g_MapItemManLoggedWaiting = false;
static bool g_MapItemManLoggedResolved = false;

// Pointer to WorldChrMan storage 
static uintptr_t g_WCMStorageAddr = 0;

// Pointer to PlayerGameData storage 
static uintptr_t g_PGDStorageAddr = 0;

static bool g_Initialized = false;
static bool g_WorldLoaded = false;
static bool g_WorldJustLoaded = false;
static bool g_WorldJustUnloaded = false;

static bool g_OverlayStarted = false;
bool g_InOurGrant = false;

static std::mutex g_PickupMutex;
static std::vector<PickupEvent> g_PickupQueue;

MapItemMan_GrantItem_t g_MapItemMan_GrantItem = nullptr;

// ---------------- EventFlag support (from old FogWall code) ----------------
// RVAs relative to sekiro.exe base
static const uint64_t SETFLAG_RVA = 0x006AAAB0;   // 0x1406AAAB0 - 0x140000000
static const uint64_t MGR_PTR_RVA = 0x03D55FE8;   // 0x143D55FE8 - 0x140000000

using TSetEventFlag = void(__fastcall*)(void* mgr, uint32_t flagId, uint8_t value);

static TSetEventFlag g_SetEventFlag = nullptr;
static void* g_EventFlagMgr = nullptr;
static bool  g_EventFlagInitialized = false;

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


enum class PlayerLifeState
{
    Unknown,
    Alive,
    Dead
};

static PlayerLifeState g_LastLifeState = PlayerLifeState::Unknown;




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

int GetResurrectionLockout()
{
    int resurrection = -1;
    if (!g_PGDStorageAddr)
    {
        g_PGDStorageAddr = FindPlayerGameDataPtr();
        if (!g_PGDStorageAddr)
        {
            return -1;
        }
        Logf("[SekiroGame] PlayerGameData storage = 0x%llX", g_PGDStorageAddr);
    }

    // 2) Read WorldChrMan*
    uintptr_t playerGameData = 0;
    if (!SafeReadPtr(g_PGDStorageAddr, playerGameData) || !playerGameData)
        return -1;

    // 3) Follow the same pointer chain you already used
    uintptr_t p1 = 0;
    if (!SafeReadPtr(playerGameData + 0x8, p1) || !p1) return -1;    
    if (!SafeReadInt(p1 + 0x150, resurrection)) return -1;
    return resurrection;
}

bool IsWorldLoaded()
{
    if (!g_WCMStorageAddr) {
        g_WCMStorageAddr = FindWorldChrManPtr(); 
        if (!g_WCMStorageAddr) {
            return false;
        }
    }

    uintptr_t worldChrMan = 0;
    if (!SafeReadPtr(g_WCMStorageAddr, worldChrMan) || !worldChrMan) {
        return false;
    }

    uintptr_t p1 = 0, p2 = 0, p3 = 0;
    if (!SafeReadPtr(worldChrMan + 0x48, p1) || !p1) {
        return false;
    }
    if (!SafeReadPtr(p1 + 0x28, p2) || !p2) {
        return false;
    }
    
    float plX = 0;
    float plY = 0;
    if (!SafeReadFloat(p2 + 0x80, plX)) {
        return false;
    }
    if (!SafeReadFloat(p2 + 0x84, plY)) {
        return false;
    }
    if (plX != 0 && plY != 0) {
        return true;
    }
    return false;

}

void UpdatePlayerDeathState()
{
    if (!IsWorldLoaded())
    {
        g_LastLifeState = PlayerLifeState::Unknown;
        return;
    }

    int hp = GetPlayerHp();    
    if (hp == -1) return;    
    bool isDead = (hp == 0);

    PlayerLifeState current =
        isDead ? PlayerLifeState::Dead
        : PlayerLifeState::Alive;

    if (g_LastLifeState == PlayerLifeState::Alive &&
        current == PlayerLifeState::Dead)
    {
        Logf("[Player] Death detected");
        Overlay_AddLog("[Player] Death detected");
        g_Pipe.SendJson("{ \"type\":\"death\", \"status\": true }");
    }

    g_LastLifeState = current;
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
        return ;

    // 3) Pointer chain (same as IsWorldLoaded)
    uintptr_t p1 = 0, p2 = 0, p3 = 0;
    if (!SafeReadPtr(worldChrMan + 0x88, p1) || !p1) return;
    if (!SafeReadPtr(p1 + 0x1FF8, p2) || !p2) return;
    if (!SafeReadPtr(p2 + 0x18, p3) || !p3) return;

    // 4) Write HP = 0
    if (!SafeWriteInt(p3 + 0x130, 0))
        return;

    Logf("[SekiroGame] KillPlayer: HP set to 0");
    return;
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

        Logf("[MapItemMan] GrantItem (initial, no hooks) = 0x%llX", (uintptr_t)g_MapItemMan_GrantItem);
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
        return (g_SetEventFlag != nullptr && g_EventFlagMgr != nullptr);

    HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
    if (!hBase)
    {
        Log("[EventFlag] sekiro.exe not loaded");
        return false;
    }

    auto base = reinterpret_cast<uint8_t*>(hBase);

    // Resolve manager pointer
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

    g_EventFlagMgr = mgr;
    g_SetEventFlag = setFlag;
    g_EventFlagInitialized = true;

    Logf("[EventFlag] Init ok: mgr=%p func=%p", g_EventFlagMgr, g_SetEventFlag);
    return true;
}

static bool SetEventFlagSafe(uint32_t flagId, bool value)
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


bool SekiroGame_Initialize()
{
    Logf("[SekiroGame] Initialize");    
    g_Initialized = true;    
    Sleep(8000);
    InitEventFlagSystem();
    InitInGameMessaging();
    g_Pipe.SendJson("{ \"type\":\"init\", \"status\": true }");
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
void SekiroGame_GrantItem(const PendingApItem& item)
{
    EnsureGrantItemResolved();

    if (!g_MapItemMan || !g_MapItemMan_GrantItem)
        return;

    const uint32_t rawGoodsId = item.itemId;
    const uint32_t encodedId = EncodeGoodsId(rawGoodsId);

    ItemBufferEntry entry{};
    entry.unk0 = 1;          // AddItem
    entry.itemId = encodedId;  // 0x40000BCC
    entry.quantity = item.quantity;
    entry.unk3 = -1;         // 0xFFFFFFFF
    entry.unk4 = 3;          // inv type

    Logf(
        "[GrantItem] Giving goodsId=%u encoded=0x%08X quantity=%u",
        rawGoodsId,
        entry.itemId,
        entry.quantity
    );

    __try
    {
        g_InOurGrant = true;
        g_MapItemMan_GrantItem(g_MapItemMan, &entry, 0,0);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        Log("[GrantItem] Exception while calling GrantItem");
    }
    g_InOurGrant = false;
}


// ---------------------------------------------------------
// Grant item + set event flag first (EventId, GoodId, Count)
// ---------------------------------------------------------
void SekiroGame_GrantItemWithEvent(uint32_t eventId, uint32_t goodsId, uint32_t count)
{
    if (eventId != 0)
    {
        bool ok = SetEventFlagSafe(eventId, true);
        if (!ok)
        {
            Logf("[GrantItemEF] Failed to set event flag %u for goods %u x%u",
                eventId, goodsId, count);
        }
    }

    PendingApItem tmp{};
    tmp.itemId = goodsId;
    tmp.quantity = count;

    Logf("[GrantItemEF] Granting goods=%u x%u (eventId=%u)",
        goodsId, count, eventId);

    SekiroGame_GrantItem(tmp);
}