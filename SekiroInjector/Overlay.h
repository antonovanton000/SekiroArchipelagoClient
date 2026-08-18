// Overlay.h
#pragma once
#include <windows.h>

// Initialize overlay window. 
// gameWindowHint можно передать nullptr — тогда возьмём активное окно.
bool Overlay_Init(HWND gameWindowHint);

// Shutdown overlay (optional)
void Overlay_Shutdown();


// High-level API for game code:

// header line: "Sekiro Archipelago Client"
void Overlay_SetHeader(const char* text);

// add a log line (will show несколько последних строк)
void Overlay_AddLog(const char* fmt, ...);
