#include "pch.h"
#include "EndingDetector.h"
#include "Log.h"
#include "Overlay.h"       
#include "Core.h" 
#include "SekiroGame.h"

bool IsEventFlagOn(uint32_t flagId)
{
    bool value = false;
    if (!GetEventFlagSafe(flagId, value))
        return false;

    return value;
}

// -----------------------------------------------------------------------------
// Ending detector internals
// -----------------------------------------------------------------------------

struct EndingDetectorState
{
    // Previous states of 683x flags, to detect rising edges (0 -> 1).
    bool prev6830 = false;
    bool prev6831 = false;
    bool prev6832 = false;
    bool prev6833 = false;

    // One-shot pending event.
    bool              hasPending = false;
    SekiroEndingType  pendingType = SekiroEndingType::None;
};

static EndingDetectorState g_EndingDetector;

// -----------------------------------------------------------------------------
// Public API
// -----------------------------------------------------------------------------

void EndingDetector_Update()
{
    // 1) Read current state of 9530 (end-game state flag).
    // We only care about 683x edges while 9530 is ON.
    bool flag9530 = IsEventFlagOn(9530);

    // 2) Read current state of ending flags.
    bool cur6830 = IsEventFlagOn(6830); // long ending #1
    bool cur6831 = IsEventFlagOn(6831); // long ending #2
    bool cur6832 = IsEventFlagOn(6832); // long ending #3
    bool cur6833 = IsEventFlagOn(6833); // Shura ending

    SekiroEndingType detected = SekiroEndingType::None;

    // 3) Detect rising edges for 683x *only* when 9530 == true.
    if (flag9530)
    {
        // Shura has priority if multiple flags ever change at the same time.
        if (!g_EndingDetector.prev6833 && cur6833)
        {
            detected = SekiroEndingType::Shura;
        }
        else if (!g_EndingDetector.prev6830 && cur6830)
        {
            detected = SekiroEndingType::ImmortalSeveranceLike;
        }
        else if (!g_EndingDetector.prev6831 && cur6831)
        {
            detected = SekiroEndingType::ImmortalSeveranceLike;
        }
        else if (!g_EndingDetector.prev6832 && cur6832)
        {
            detected = SekiroEndingType::ImmortalSeveranceLike;
        }
    }

    // 4) Store current states for the next tick.
    g_EndingDetector.prev6830 = cur6830;
    g_EndingDetector.prev6831 = cur6831;
    g_EndingDetector.prev6832 = cur6832;
    g_EndingDetector.prev6833 = cur6833;

    // 5) If we detected a new ending, store it for one-shot retrieval.
    if (detected != SekiroEndingType::None)
    {
        g_EndingDetector.hasPending = true;
        g_EndingDetector.pendingType = detected;

        Logf("[EndingDetector] Ending detected: %s (9530=1, 683x rising edge)",
            detected == SekiroEndingType::Shura ? "Shura" : "ImmortalSeveranceLike");
    }
}

bool EndingDetector_JustFinished(SekiroEndingType& outType)
{
    if (!g_EndingDetector.hasPending)
        return false;

    outType = g_EndingDetector.pendingType;
    g_EndingDetector.hasPending = false;
    return true;
}