// Core.h
#pragma once

extern bool g_IsDebug;
DWORD WINAPI CoreThread(LPVOID lpParameter);


constexpr int kMaxForeignLots = 750;

extern uint32_t g_ForeignPickupLots[kMaxForeignLots];
extern int      g_ForeignPickupLotsCount;
extern bool     g_ForeignPickupLotsInitialized;

extern uint32_t g_ForeignShopLots[kMaxForeignLots];
extern int      g_ForeignShopLotsCount;
extern bool     g_ForeignShopLotsInitialized;