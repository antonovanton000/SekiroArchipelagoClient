#pragma once
#include <cstdint>
#include "SekiroTypes.h"

// Lifecycle
bool SekiroGame_Initialize();
void SekiroGame_Update();

// Item granting
void SekiroGame_GrantItem(const PendingApItem& item);

// New: grant item + set event flag first
void SekiroGame_GrantItemWithEvent(uint32_t eventId, uint32_t goodsId, uint32_t count);

bool SetEventFlagSafe(uint32_t flagId, bool value);
bool GetEventFlagSafe(uint32_t flagId, bool& outValue);

// Low-level helpers (can stay public for now)
bool IsWorldLoaded();

//Kill player for DeathLink
void SekiroGame_KillPlayer();

bool SekiroGame_SetEnemyAiDisabled(bool disabled);
bool SekiroGame_SetOneHitKillEnabled(bool enabled);
