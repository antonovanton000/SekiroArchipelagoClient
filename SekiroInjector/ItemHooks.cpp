#include "pch.h"
#include "Log.h"
#include "ItemHooks.h"
#include "SekiroGame.h"      // g_InOurGrant, g_MapItemMan_GrantItem, ItemBufferEntry, MapItemMan_GrantItem_t, MapItemMan_AwardItemLot_t, ItemEvent
#include <Windows.h>
#include "MinHook.h"
#include "Utils.h"
#include "Overlay.h"
#include "PipeConnection.h"
#include "Core.h"
#include <mutex>
#include <queue>

extern PipeConnection g_Pipe;

static MapItemMan_GrantItem_t g_GrantItem_Original = nullptr;
ShopFunc_t g_ShopFunc_Original = nullptr;

using BuildItemFromLot_t = void(__fastcall*)(void* outStruct, uint32_t lotId);
static BuildItemFromLot_t g_BuildItemFromLot_Original = nullptr;


static uint32_t g_CurrentItemLotId = 0;
static bool g_CurrentLotValid = false;

static bool g_InterruptItemAcquiring = false;
static bool g_PickupHandled = false;
thread_local uint32_t g_LastRewardLotId = 0;


// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

bool IsAllowedGoods(uint32_t goodsId)
{
	return g_AllowedGoods.find(goodsId) != g_AllowedGoods.end();
}

void OnLootDetected(uint32_t lotIndex, uint32_t goodid, bool isFromShop, bool isForeign)
{
	char buf[256];
	sprintf_s(
		buf,
		sizeof(buf),
		"{ \"type\":\"item_picked\", \"lot_index\":%u, \"goods_id\":%u, \"is_from_shop\":%s, \"is_foreign\":%s }",
		lotIndex,	
		goodid,
		isFromShop ? "true" : "false",
		isForeign ? "true" : "false"
	);

	g_Pipe.SendJson(std::string(buf));
}

void __fastcall Hooked_BuildItemFromLot(void* outStruct, uint32_t lotId)
{
	// save lotid
	g_LastRewardLotId = lotId;
	g_BuildItemFromLot_Original(outStruct, lotId);
}

// Тип целевой функции (91FCF0)
using PickupExec_t = void(__fastcall*)(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag,
	uint8_t   a4
	);

// Тип внутренней функции 920330, которая возвращает pickupStruct
using GetPickupStruct_t = uintptr_t(__fastcall*)(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag
	);

// Original 91FCF0
static PickupExec_t g_PickupExec_Original = nullptr;

// Funciton 920330
static GetPickupStruct_t g_GetPickupStruct = nullptr;

void __fastcall Hooked_PickupExec(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag,
	uint8_t   a4)
{
	// get pickup struct
	if (g_GetPickupStruct)
	{
		uintptr_t pickupStruct = g_GetPickupStruct(mapItemMan, a2, flag);

		if (pickupStruct)
		{
			uint32_t encodedId = *(uint32_t*)(pickupStruct + 0x58);
			uint32_t quantity = *(uint32_t*)(pickupStruct + 0x5C);
			uint32_t lotId = *(uint32_t*)(pickupStruct + 0xD4);
			uint32_t goodsId = DecodeGoodsId(encodedId);
			// little validation
			if (quantity > 0 && lotId != 0 && lotId != 0xFFFFFFFF)
			{
				bool isForeign = IsForeignPickupLot(lotId);
				g_InterruptItemAcquiring = isForeign;
				g_PickupHandled = true;
				OnLootDetected(lotId, goodsId, false, isForeign);

				Logf("[PickupExec] staged lot=%u goodId=%u foreign=%s", lotId, goodsId, isForeign ? "true" : "false");
				Overlay_AddLog("[PickupExec] staged lot=%u goodId=%u foreign=%s", lotId, goodsId, isForeign ? "true" : "false");
			}
		}
	}

	g_PickupExec_Original(mapItemMan, a2, flag, a4);
}


void __fastcall Hooked_MapItemMan_GrantItem(
	uintptr_t mapItemMan,
	ItemBufferEntry* entry,
	uint64_t ctx,
	uint64_t a4)
{
	uint32_t raw = entry->itemId;
	if ((raw & 0xF0000000) != 0x40000000)
	{
		g_GrantItem_Original(mapItemMan, entry, ctx, a4);
		return;
	}

	if (g_InOurGrant || !entry)
	{
		g_GrantItem_Original(mapItemMan, entry, ctx, a4);
		return;
	}

	uint32_t goodsId = DecodeGoodsId(entry->itemId);
	uint32_t qty = entry->quantity;
	bool isAllowedKeyItem = IsAllowedGoods(goodsId);

	if (g_LastRewardLotId != 0 && !isAllowedKeyItem && !g_PickupHandled)
	{
		bool isForeign = IsForeignPickupLot(g_LastRewardLotId);
		g_InterruptItemAcquiring = isForeign;

		OnLootDetected(g_LastRewardLotId, goodsId, false, isForeign);

		Logf("[Reward] staged lot=%u goodsId=%u foreign=%s", g_LastRewardLotId, goodsId, isForeign ? "true" : "false");
		Overlay_AddLog("[Reward] staged lot=%u goodsId=%u foreign=%s", g_LastRewardLotId, goodsId, isForeign ? "true" : "false");

		g_LastRewardLotId = 0;
	}
	g_PickupHandled = false;
	g_GrantItem_Original(mapItemMan, entry, ctx, a4);
}



using ShopPurchaseEntry_t = __int64(__fastcall*)(void* shopRuntime);

// Original function sekiro.exe+DD3FA0
static ShopPurchaseEntry_t g_ShopPurchaseEntry_Original = nullptr;

__int64 __fastcall Hooked_ShopPurchaseEntry(void* shopEntry)
{
	uint8_t* base = (uint8_t*)shopEntry;

	uint32_t purchaseCount = *(uint32_t*)(base + 0x00);
	uint32_t packed = *(uint32_t*)(base + 0x40);

	bool isGoods = (packed & 0xF0000000u) == 0x40000000u;

	uint32_t goodsId = isGoods ? (packed & 0x0FFFFFFF) : 0;
	uint32_t lineupId = *(uint32_t*)(base + 0x48);

	bool isForeign = IsForeignShopLot(lineupId);
	g_InterruptItemAcquiring = isForeign;

	OnLootDetected(lineupId, goodsId, true, isForeign);

	Logf("[Shop] staged lot=%u goodsId=%u foreign=%s", lineupId, goodsId, isForeign ? "true" : "false");
	Overlay_AddLog("[Shop] staged lot=%u goodsId=%u foreign=%s", lineupId, goodsId, isForeign ? "true" : "false");

	return g_ShopPurchaseEntry_Original(shopEntry);
}

using CommitItem_t = int(__fastcall*)(
	void* rcx,
	int   edx,
	int   goodsId,
	int   qty
	);

static CommitItem_t g_CommitItem_Original = nullptr;

int __fastcall Hooked_CommitItem(
	void* rcx,
	int   edx,
	int   goodsId,
	int   qty)
{	
	if (!g_InterruptItemAcquiring)
		return g_CommitItem_Original(rcx, edx, goodsId, qty);
	else
	{
		g_InterruptItemAcquiring = false;
		return 0;	
	}
}

// -----------------------------------------------------------------------------
// SetItemCount (might be helpfull direct change count in inventory)
// -----------------------------------------------------------------------------

// __fastcall: RCX = slot, EDX = newCount
//using SetItemCount_t = void(__fastcall*)(void* slot, uint32_t newCount);
//static SetItemCount_t g_SetItemCount_Original = nullptr;
//
//void __fastcall SetItemCount_Hook(void* slot, uint32_t newCount)
//{
//	auto* base = reinterpret_cast<uint8_t*>(slot);
//
//	// reading old count and goodsId directly from slot struct
//	uint32_t oldCount = *reinterpret_cast<uint32_t*>(base + 0x08);
//	uint32_t packedId = *reinterpret_cast<uint32_t*>(base + 0x00);
//	uint32_t goodsId = packedId & 0x0FFFFFFF;  // unpack goodsId
//
//	// calling original ( mov [rcx+08], edx; ret)
//	g_SetItemCount_Original(slot, newCount);
//
//	int delta = static_cast<int>(newCount) - static_cast<int>(oldCount);
//
//	if (delta > 0)
//	{
//		Logf("[ItemChange] goodsId=%u +%d (old=%u new=%u)",
//			goodsId, delta, oldCount, newCount);
//
//		OnLootDetected(0, goodsId, delta, false);
//		Overlay_AddLog("[Shop] goods=%u x%u", goodsId, delta);
//	}
//}

// -----------------------------------------------------------------------------
// MinHook initialization
// -----------------------------------------------------------------------------

bool ItemHooks_Initialize()
{
	static bool s_AlreadyInitialized = false;
	if (s_AlreadyInitialized)
		return true;

	Log("[ItemHooks] Initializing MinHook...");

	HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
	if (!hMod)
	{
		Log("[ItemHooks] sekiro.exe module not found");
		return false;
	}

	uintptr_t base = reinterpret_cast<uintptr_t>(hMod);

	uintptr_t buildItemAddr = base + 0x913DF0;

	MH_STATUS status = MH_CreateHook(
		reinterpret_cast<void*>(buildItemAddr),
		&Hooked_BuildItemFromLot,
		reinterpret_cast<void**>(&g_BuildItemFromLot_Original));

	MH_EnableHook(reinterpret_cast<void*>(buildItemAddr));

	Logf("[ItemHooks] BuildItemFromLot hook = 0x%llX",
		static_cast<unsigned long long>(buildItemAddr));


	uintptr_t getPickupAddr = base + 0x920330;
	g_GetPickupStruct = reinterpret_cast<GetPickupStruct_t>(getPickupAddr);
	Logf("[ItemHooks] GetPickupStruct = 0x%llX",
		static_cast<unsigned long long>(getPickupAddr));

	uintptr_t pickupExecTarget = base + 0x91FCF0;
	Logf("[ItemHooks] PickupExec target = 0x%llX",
		static_cast<unsigned long long>(pickupExecTarget));

	status = MH_CreateHook(
		reinterpret_cast<LPVOID>(pickupExecTarget),
		reinterpret_cast<LPVOID>(&Hooked_PickupExec),
		reinterpret_cast<LPVOID*>(&g_PickupExec_Original));

	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_CreateHook(PickupExec) failed: %d", (int)status);
		return false;
	}

	status = MH_EnableHook(reinterpret_cast<LPVOID>(pickupExecTarget));
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_EnableHook(PickupExec) failed: %d", (int)status);
		return false;
	}

	Logf("[ItemHooks] PickupExec hook enabled. target=0x%llX orig=0x%llX",
		static_cast<unsigned long long>(pickupExecTarget),
		static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(g_PickupExec_Original)));



	uintptr_t grantItemAddr = base + 0x91C970;

	Logf("[ItemHooks] GrantItem target = 0x%llX",
		static_cast<unsigned long long>(grantItemAddr));

	status = MH_CreateHook(
		reinterpret_cast<LPVOID>(grantItemAddr),
		reinterpret_cast<LPVOID>(&Hooked_MapItemMan_GrantItem),
		reinterpret_cast<LPVOID*>(&g_GrantItem_Original));

	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_CreateHook(GrantItem) failed: %d", (int)status);
		return false;
	}

	status = MH_EnableHook(reinterpret_cast<LPVOID>(grantItemAddr));
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_EnableHook(GrantItem) failed: %d", (int)status);
		return false;
	}

	uintptr_t commitItemAddr = base + 0x79BF90;

	Logf("[ItemHooks] CommitItemAddr = 0x%llX",
		(unsigned long long)commitItemAddr);

	if (MH_CreateHook(
		(LPVOID)commitItemAddr,
		&Hooked_CommitItem,
		(LPVOID*)&g_CommitItem_Original) != MH_OK)
	{
		Log("[ItemHooks] MH_CreateHook(ShopCommitItem) failed");
		return false;
	}

	if (MH_EnableHook((LPVOID)commitItemAddr) != MH_OK)
	{
		Log("[ItemHooks] MH_EnableHook(ShopCommitItem) failed");
		return false;
	}

	uintptr_t shopEntryTarget = base + 0xDD3FA0;

	Logf("[ItemHooks] ShopPurchaseEntry target = 0x%llX",
		static_cast<unsigned long long>(shopEntryTarget));

	if (MH_CreateHook(
		reinterpret_cast<LPVOID>(shopEntryTarget),
		reinterpret_cast<LPVOID>(&Hooked_ShopPurchaseEntry),
		reinterpret_cast<LPVOID*>(&g_ShopPurchaseEntry_Original)) != MH_OK)
	{
		Log("[ItemHooks] MH_CreateHook(ShopPurchaseEntry) failed");
		return false;
	}

	if (MH_EnableHook(reinterpret_cast<LPVOID>(shopEntryTarget)) != MH_OK)
	{
		Log("[ItemHooks] MH_EnableHook(ShopPurchaseEntry) failed");
		return false;
	}

	Logf("[ItemHooks] GrantItem original = 0x%llX",
		static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(g_GrantItem_Original)));

	s_AlreadyInitialized = true;
	return true;
}

// -----------------------------------------------------------------------------
// Shutdown
// -----------------------------------------------------------------------------

void ItemHooks_Shutdown()
{
	MH_DisableHook(MH_ALL_HOOKS);
	MH_Uninitialize();
}
