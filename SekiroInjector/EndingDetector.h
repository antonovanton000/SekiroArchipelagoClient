#pragma once
#include <cstdint>

// -----------------------------------------------------------------------------
// External symbols from your existing EventFlag system.
// You should already have these defined somewhere in your code.
// -----------------------------------------------------------------------------

// SetEventFlag: already used by your SetEventFlagSafe
using FnSetEventFlag = void(__fastcall*)(void* mgr, uint32_t flagId, uint8_t value);

// GetEventFlag: new function pointer for reading flags
using FnGetEventFlag = uint8_t(__fastcall*)(void* mgr, uint32_t flagId);

extern FnSetEventFlag g_SetEventFlag;
extern FnGetEventFlag g_GetEventFlag;
extern void* g_EventFlagMgr;

// Your existing helpers
bool InitEventFlagSystem();
void Logf(const char* fmt, ...);

// -----------------------------------------------------------------------------
// Public API: safe flag read helpers
// -----------------------------------------------------------------------------

// Safely read an event flag value (0/1) from the game's EventFlag manager.
// Returns true on success, false on failure (in which case outValue is false).
bool GetEventFlagSafe(uint32_t flagId, bool& outValue);

// Convenience helper: returns true if flag is ON (1), false otherwise.
// If reading fails, returns false.
bool IsEventFlagOn(uint32_t flagId);

// -----------------------------------------------------------------------------
// Ending detector
// -----------------------------------------------------------------------------

// High-level classification of Sekiro endings that we care about.
enum class SekiroEndingType
{
    None = 0,

    // Any long ending which uses flags 6830 / 6831 / 6832.
    // For your use case this group is "Immortal Severance-like".
    ImmortalSeveranceLike,

    // Shura ending (flag 6833).
    Shura
};

// Call this once per frame / game tick.
// It will:
//   - read flags 9530 and 6830..6833,
//   - detect rising edges for 683x when 9530 == true,
//   - store a one-shot "ending detected" event internally.
void EndingDetector_Update();

// Check if an ending has just been detected since the last call.
// Returns true exactly once per detected ending, and writes the type to outType.
bool EndingDetector_JustFinished(SekiroEndingType& outType);