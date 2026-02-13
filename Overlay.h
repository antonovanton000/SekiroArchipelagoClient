// Overlay.h
#pragma once
#include <windows.h>

// Initialize overlay window. 
// gameWindowHint можно передать nullptr — тогда возьмём активное окно.
bool Overlay_Init(HWND gameWindowHint);

// Shutdown overlay (optional)
void Overlay_Shutdown();

// Called periodically from CoreThread: pumps messages + redraws overlay.
void Overlay_Update();

// High-level API for game code:

// header line: "Sekiro Archipelago Client v1.0.0 (alpha)"
void Overlay_SetHeader(const char* text);

// toggle "world loaded" state
void Overlay_SetWorldLoaded(bool loaded);

// add a log line (will show несколько последних строк)
void Overlay_AddLog(const char* fmt, ...);
