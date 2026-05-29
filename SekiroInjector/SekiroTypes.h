#pragma once
#include <cstdint>
#include <unordered_set>
#include <cstdint>

// Simple Archipelago pending item (in-game item id, not lot id)
struct PendingApItem
{
    uint32_t itemId;
    uint32_t quantity;
};

// Minimal item entry passed into MapItemMan::GrantItem / AddItem
struct ItemBufferEntry
{
    uint32_t unk0;      // 0x00 - observed 1 in all our logs
    uint32_t itemId;    // 0x04 - encoded item id (category + paramId)
    uint32_t quantity;  // 0x08
    int32_t  unk3;      // 0x0C - observed -1 (0xFFFFFFFF)
    uint32_t unk4;      // 0x10 - observed 3
    // The real struct is larger, but for our use these 0x14 bytes are enough.
};
static_assert(sizeof(ItemBufferEntry) == 0x14, "ItemBufferEntry size mismatch");


struct PickupEvent
{
    uint32_t goodsId;   // raw EquipParamGoods ID
    uint32_t quantity;  // how many items game grants
};


using MapItemMan_GrantItem_t = void(__fastcall*)(uintptr_t mapItemMan, ItemBufferEntry* entry, uint64_t ctx, uint64_t a4);

extern MapItemMan_GrantItem_t g_MapItemMan_GrantItem;
extern thread_local bool g_InOurGrant;

using ShopFunc_t = 
__int64(__fastcall*)(void* itemData, int count, void* a3, void* a4);
extern ShopFunc_t g_ShopFunc_Original;

struct PendingItem
{
    bool     valid = false;
    uint32_t lotId = 0;
    uint32_t goodsId = 0;
    uint32_t quantity = 0;
	bool     isShop = false;
	bool     foreign = false;
};

const std::unordered_set<uint32_t> g_AllowedGoods =
{
	1000,   // Spirit Emblem
	1001,   // Red Spirit Emblem
	1100,   // Regenerative Power
	1101,   // Regenerative Power Fragment
	1110,   // Temporary Regenerative Power
	1200,   // Skill Point
	2000,   // Resurrection
	2300,   // Kusabimaru
	3000,   // Healing Gourd
	5510,   // Dancing Dragon Mask	
	5300,   // Remnant: Gyoubu
	5301,   // Remnant: Lady Butterfly
	5302,   // Remnant: Genichiro
	5303,   // Remnant: Screen Monkeys
	5304,   // Remnant: Guardian Ape
	5305,   // Remnant: Corrupted Monk
	5306,   // Remnant: Great Shinobi
	5307,   // Remnant: Foster Father
	5308,   // Remnant: True Monk
	5309,   // Remnant: Divine Dragon
	5310,   // Remnant: Hatred Demon
	5311,   // Remnant: Saint Isshin
	5312,   // Remnant: Isshin Ashina
	5313,   // Remnant: Headless Ape
	5320,   // Remnant: Inner Genichiro
	5321,   // Remnant: Inner Father
	5322,   // Remnant: Inner Isshin
	//131000, // Protagonist body with sword
	//102000, // Protagonist body with prothesis
	4100,	//First Prayer Necklace
	4101,	//Second Prayer Necklace
	4102,	//Third Prayer Necklace
	4103,	//Fourth Prayer Necklace
	4104,	//Fifth Prayer Necklace
	4105,	//Sixth Prayer Necklace
	4106,	//Seventh Prayer Necklace
	4107,	//Eighth Prayer Necklace
	4108,	//Ninth Prayer Necklace
	4109,	//Final Prayer Necklace
	9500,   //Rot Essence: Sculptor
	9501,   //Rot Essence: Newcomer
	9502,   //Rot Essence: Black Hat
	9503,   //Rot Essence: Lost Child
	9504,   //Rot Essence: Charmed One
	9505,   //Rot Essence: Surgeons
	9510,   //Rot Essence: Fine Son
	9511,   //Rot Essence: Thirsty One
	9515,   //Rot Essence: Timid Maid
	9516,   //Rot Essence: Faithful One
	9520,   //Rot Essence: Crow Mob
	9521,   //Rot Essence: Wartorn Mob
	9522,   //Rot Essence: Jail Mob
	9523,   //Rot Essence: Toxic Mob
	9524,   //Rot Essence: Pious Mob
	9525,   //Rot Essence: Drunk Mob
	9526   //Rot Essence: Info Broker
};
