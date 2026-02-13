#pragma once

#include <cstdint>

// Simple debug overlay state shared between game logic and renderer.
struct OverlayState
{
    bool     worldLoaded = false;
    bool     mapItemManReady = false;

    uint32_t lastItemId = 0;
    uint32_t lastItemQuantity = 0;
    uint64_t lastItemTimeMs = 0;   // GetTickCount64()
};

extern OverlayState g_OverlayState;