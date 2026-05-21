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
#include <vector>
#include <mutex>
#include <queue>

extern PipeConnection g_Pipe;
static MapItemMan_GrantItem_t g_GrantItem_Original = nullptr;
ShopFunc_t g_ShopFunc_Original = nullptr;

using BuildItemFromLot_t = void(__fastcall*)(void* outStruct, uint32_t lotId);
static BuildItemFromLot_t g_BuildItemFromLot_Original = nullptr;
static bool g_PickupHandled = false;
thread_local uint32_t g_LastTrackedRewardLotId = 0;
thread_local uint32_t g_LastRewardParamReportedLotId = 0;
thread_local uint32_t g_LastRewardParamReportedGoodsId = 0;
struct RewardContext
{
	bool     active = false;
	uint32_t lotId = 0;
};

thread_local RewardContext g_RewardCtx;

using GetRewardStruct_t = void* (__fastcall*)(void* outStruct, uint32_t lotId);

using RewardExec_t = void(__fastcall*)(uintptr_t ctx, uint32_t lotId, uint8_t flag1, uint8_t flag2);

using RewardParamInit_t = void(__fastcall*)(void* rewardParam, int mode);

static RewardExec_t       g_RewardExec_Original = nullptr;
static RewardParamInit_t  g_RewardParamInit_Original = nullptr;
static bool g_InterruptItemAcquiring = false;

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

bool IsAllowedGoods(uint32_t goodsId)
{
	return g_AllowedGoods.find(goodsId) != g_AllowedGoods.end();
}

bool IsTrackedRewardLot(uint32_t lotId)
{
	return lotId == 51300  // DT: Hidden Tooth - complete Hanbei's quest
		|| lotId == 52810; // MV: Treasure Carp Scale - Head Priest's house, enemy drop
}

void OnLootDetected(uint32_t lotIndex, uint32_t goodid, bool isFromShop)
{
	char buf[256];
	sprintf_s(
		buf,
		sizeof(buf),
		"{ \"type\":\"item_picked\", \"lot_index\":%u, \"goods_id\":%u, \"is_from_shop\":%s }",
		lotIndex,
		goodid,
		isFromShop ? "true" : "false"
	);
	g_Pipe.SendJson(std::string(buf));	
}

void __fastcall Hooked_RewardExec(
	uintptr_t ctx,
	uint32_t  lotId,
	uint8_t   flag1,
	uint8_t   flag2)
{
	Logf("[RewardExec] called lot=%u", lotId);

	g_RewardCtx.active = true;
	g_RewardCtx.lotId = lotId;

	g_RewardExec_Original(ctx, lotId, flag1, flag2);

	g_RewardCtx.active = false;
	g_RewardCtx.lotId = 0;
}


//////////////////////////////////////////////////////////
// sekiro.exe+C3BA30
//////////////////////////////////////////////////////////
void __fastcall Hooked_RewardParamInit(void* rewardParam, int mode)
{
	g_RewardParamInit_Original(rewardParam, mode);

	if (!g_RewardCtx.active)
		return;

	if (!IsWorldLoaded())
		return;

	if (!rewardParam)
		return;

	uint8_t* base = reinterpret_cast<uint8_t*>(rewardParam);
	uint32_t encodedId = *(uint32_t*)(base + 0x38);
	uint32_t quantity = *(uint32_t*)(base + 0x3C);

	if (encodedId == 0 || quantity == 0)
		return;

	uint32_t goodsId = DecodeGoodsId(encodedId);
	uint32_t lotId = g_RewardCtx.lotId;
	bool isAllowedItem = IsAllowedGoods(goodsId);

	if (isAllowedItem && !IsTrackedRewardLot(lotId))
	{
		Logf("[Reward] ignored lot=%u goods=%u qty=%u allowed=%s", lotId, goodsId, quantity, isAllowedItem ? "true" : "false");
		return;
	}

	OnLootDetected(lotId, goodsId, false);
	g_LastRewardParamReportedLotId = lotId;
	g_LastRewardParamReportedGoodsId = goodsId;
	Logf("[Reward] staged tracked lot=%u goods=%u qty=%u allowed=%s", lotId, goodsId, quantity, isAllowedItem ? "true" : "false");
	Overlay_AddLog("[Reward] staged lot=%u goods=%u", lotId, goodsId);
}

void __fastcall Hooked_BuildItemFromLot(void* outStruct, uint32_t lotId)
{
	g_LastTrackedRewardLotId = IsTrackedRewardLot(lotId) ? lotId : 0;
	if (g_LastTrackedRewardLotId != 0)
		Logf("[BuildItemFromLot] tracked reward lot=%u", g_LastTrackedRewardLotId);

	g_BuildItemFromLot_Original(outStruct, lotId);
}

//////////////////////////////////////////////////////////
using PickupExec_t = void(__fastcall*)(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag,
	uint8_t   a4
	);

// 920330 pickupStruct
using GetPickupStruct_t = uintptr_t(__fastcall*)(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag
	);

// Original 91FCF0
static PickupExec_t g_PickupExec_Original = nullptr;

// Funciton 920330
static GetPickupStruct_t g_GetPickupStruct = nullptr;

struct PickupRead
{
	bool valid = false;
	uint32_t packed = 0;
	uint32_t quantity = 0;
	uint32_t lotId = 0;
	uint32_t goodsId = 0;
	bool isAllowedItem = false;
};

static PickupRead ReadPickup(uintptr_t mapItemMan, uint32_t a2, uint8_t flag)
{
	PickupRead read;
	if (!g_GetPickupStruct)
		return read;

	uintptr_t pickupStruct = g_GetPickupStruct(mapItemMan, a2, flag);
	if (!pickupStruct)
		return read;

	read.packed = *(uint32_t*)(pickupStruct + 0x58);
	read.quantity = *(uint32_t*)(pickupStruct + 0x5C);
	read.lotId = *(uint32_t*)(pickupStruct + 0xD4);
	read.goodsId = DecodeGoodsId(read.packed);
	read.isAllowedItem = IsAllowedGoods(read.goodsId);
	read.valid = read.quantity > 0 && read.lotId != 0 && read.lotId != 0xFFFFFFFF;
	return read;
}

void __fastcall Hooked_PickupExec(
	uintptr_t mapItemMan,
	uint32_t  a2,
	uint8_t   flag,
	uint8_t   a4)
{
	PickupRead before = ReadPickup(mapItemMan, a2, flag);
	g_PickupExec_Original(mapItemMan, a2, flag, a4);
	PickupRead after = ReadPickup(mapItemMan, a2, flag);

	Logf(
		"[PickupExec] probe a2=%u flag=%u before(valid=%d lot=%u good=%u qty=%u allowed=%d) after(valid=%d lot=%u good=%u qty=%u allowed=%d)",
		a2,
		flag,
		before.valid ? 1 : 0,
		before.lotId,
		before.goodsId,
		before.quantity,
		before.isAllowedItem ? 1 : 0,
		after.valid ? 1 : 0,
		after.lotId,
		after.goodsId,
		after.quantity,
		after.isAllowedItem ? 1 : 0);

	PickupRead picked = after.valid ? after : before;
	if (picked.valid && !picked.isAllowedItem)
	{
		OnLootDetected(picked.lotId, picked.goodsId, false);
		Logf("[PickupExec] staged lot=%u goodId=%u", picked.lotId, picked.goodsId);
		Overlay_AddLog("[PickupExec] staged lot=%u goodId=%u", picked.lotId, picked.goodsId);
	}
}

void __fastcall Hooked_MapItemMan_GrantItem(
	uintptr_t mapItemMan,
	ItemBufferEntry* entry,
	uint64_t ctx,
	uint64_t a4)
{
	if (!entry)
	{
		g_GrantItem_Original(mapItemMan, entry, ctx, a4);
		return;
	}

	uint32_t raw = entry->itemId;
	if ((raw & 0xF0000000) != 0x40000000)
	{
		g_GrantItem_Original(mapItemMan, entry, ctx, a4);
		return;
	}

	if (!g_InOurGrant && !g_PickupHandled && IsTrackedRewardLot(g_LastTrackedRewardLotId))
	{
		uint32_t goodsId = DecodeGoodsId(entry->itemId);
		if (g_LastRewardParamReportedLotId == g_LastTrackedRewardLotId && g_LastRewardParamReportedGoodsId == goodsId)
		{
			Logf("[GrantItemReward] skipped duplicate lot=%u goods=%u qty=%u", g_LastTrackedRewardLotId, goodsId, entry->quantity);
			g_LastRewardParamReportedLotId = 0;
			g_LastRewardParamReportedGoodsId = 0;
		}
		else
		{
			OnLootDetected(g_LastTrackedRewardLotId, goodsId, false);
			Logf("[GrantItemReward] staged tracked lot=%u goods=%u qty=%u", g_LastTrackedRewardLotId, goodsId, entry->quantity);
			Overlay_AddLog("[Reward] staged lot=%u goods=%u", g_LastTrackedRewardLotId, goodsId);
		}
	}

	g_LastTrackedRewardLotId = 0;
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
	if (isGoods)
	{
		OnLootDetected(lineupId, goodsId, true);
		Logf("[Shop] staged lot=%u goodsId=%u", lineupId, goodsId);
		Overlay_AddLog("[Shop] staged lot=%u goodsId=%u", lineupId, goodsId);
	}

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
	if (edx == 0x40000000 && goodsId >= 6000000)
		return g_CommitItem_Original(rcx, edx, goodsId, 0);

	return g_CommitItem_Original(rcx, edx, goodsId, qty);	
}



//-----------------------------------------------------------------------------
//SetItemCount (might be helpfull direct change count in inventory)
//-----------------------------------------------------------------------------

// __fastcall: RCX = slot, EDX = newCount
//using SetItemCount_t = void(__fastcall*)(void* slot, uint32_t newCount);
//SetItemCount_t g_SetItemCount_Original = nullptr;
//
//void __fastcall SetItemCount_Hook(void* slot, uint32_t newCount)
//{
//	auto* base = reinterpret_cast<uint8_t*>(slot);
//	uint32_t packedId = *reinterpret_cast<uint32_t*>(base + 0x04);
//	uint32_t goodsId = packedId & 0x0FFFFFFF;
//	uint32_t category = packedId & 0xF0000000u;
//	
//	if (category == 0x40000000u && goodsId >= 6000000)
//		newCount = 0;
//
//	//Logf("[ItemHooks] SetItemCount packed=0x%08X cat=0x%X goodsId=%u", packedId, category >> 28, goodsId);
//	g_SetItemCount_Original(slot, newCount);
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
	MH_STATUS status = MH_OK;

	uintptr_t buildItemAddr = base + 0x913DF0;
	status = MH_CreateHook(
		reinterpret_cast<void*>(buildItemAddr),
		&Hooked_BuildItemFromLot,
		reinterpret_cast<void**>(&g_BuildItemFromLot_Original));

	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_CreateHook(BuildItemFromLot) failed: %d", (int)status);
		return false;
	}

	status = MH_EnableHook(reinterpret_cast<void*>(buildItemAddr));
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_EnableHook(BuildItemFromLot) failed: %d", (int)status);
		return false;
	}

	Logf("[ItemHooks] BuildItemFromLot hook enabled. target=0x%llX",
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


	uintptr_t rewardExecTarget = base + 0x918810;

	Logf("[ItemHooks] RewardExec target = 0x%llX",
		static_cast<unsigned long long>(rewardExecTarget));

	status = MH_CreateHook(
		reinterpret_cast<LPVOID>(rewardExecTarget),
		reinterpret_cast<LPVOID>(&Hooked_RewardExec),
		reinterpret_cast<LPVOID*>(&g_RewardExec_Original)
	);

	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_CreateHook(RewardExec) failed: %d", (int)status);
		return false;
	}

	status = MH_EnableHook(reinterpret_cast<LPVOID>(rewardExecTarget));
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_EnableHook(RewardExec) failed: %d", (int)status);
		return false;
	}

	Logf("[ItemHooks] RewardExec hook enabled. target=0x%llX orig=0x%llX",
		static_cast<unsigned long long>(rewardExecTarget),
		static_cast<unsigned long long>(
			reinterpret_cast<uintptr_t>(g_RewardExec_Original)));


	uintptr_t rewardParamTarget = base + 0xC3BA30;
	Logf("[ItemHooks] RewardParamInit target = 0x%llX",
		(unsigned long long)rewardParamTarget);

	status = MH_CreateHook(
		reinterpret_cast<LPVOID>(rewardParamTarget),
		reinterpret_cast<LPVOID>(&Hooked_RewardParamInit),
		reinterpret_cast<LPVOID*>(&g_RewardParamInit_Original)
	);
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_CreateHook(RewardParamInit) failed: %d", (int)status);
		return false;
	}

	status = MH_EnableHook(reinterpret_cast<LPVOID>(rewardParamTarget));
	if (status != MH_OK)
	{
		Logf("[ItemHooks] MH_EnableHook(RewardParamInit) failed: %d", (int)status);
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

	Logf("[ItemHooks] GrantItem hook enabled. target=0x%llX orig=0x%llX",
		static_cast<unsigned long long>(grantItemAddr),
		static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(g_GrantItem_Original)));

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

	

	//uintptr_t setItemCountAddr = base + 0xC3CF50;

	//status = MH_CreateHook(
	//	reinterpret_cast<LPVOID>(setItemCountAddr),
	//	reinterpret_cast<LPVOID>(&SetItemCount_Hook),
	//	reinterpret_cast<LPVOID*>(&g_SetItemCount_Original)
	//);
	//if (status != MH_OK)
	//{
	//	Logf("[ItemHooks] MH_CreateHook(SetItemCount_Hook) failed: %d", (int)status);
	//	return false;
	//}

	//status = MH_EnableHook(reinterpret_cast<LPVOID>(setItemCountAddr));
	//if (status != MH_OK)
	//{
	//	Logf("[ItemHooks] MH_EnableHook(SetItemCount_Hook) failed: %d", (int)status);
	//	return false;
	//}	
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
