#pragma once
#include <cstdint>

struct ItemEvent
{
    uint32_t goodsId;      // EquipParamGoods ID (3020 etc.)
    uint32_t quantity;     // Count
    uint32_t encodedId;    // 0x4??????? (like in entry.itemId)
    uint64_t ctx;
    uint64_t a4;
    uint32_t lotId;    //ItemLotId
};

bool ItemHooks_Initialize();
void ItemHooks_Shutdown();

// Serializes AP item grants against native pickup/shop operations.
// Reward/suction delivery remains outside this barrier.
bool ItemHooks_TryBeginApGrant();
void ItemHooks_EndApGrant();

// Experimental: remove foreign synthetic goods after their native pickup
// notification has had time to complete.
void ItemHooks_ProcessPendingForeignRemovals();
