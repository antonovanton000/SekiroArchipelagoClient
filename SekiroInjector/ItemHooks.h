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