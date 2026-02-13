// dllmain.cpp
#include "pch.h"
#include <windows.h>
#include <thread>
#include <chrono>
#include <atomic>
#include <fstream>
#include <string>
#include "SekiroGame.h"
#include <Psapi.h>
#pragma comment(lib, "Psapi.lib")
#include "Log.h"
#include "Core.h"

static HMODULE g_hOriginalDInput8 = nullptr;
static std::atomic<bool> g_CoreStarted = false;


// ========================================================
// Helper: load original dinput8.dll
// ========================================================
void LoadOriginalDInput8()
{
	if (g_hOriginalDInput8)
		return;

	char sysDir[MAX_PATH];
	GetSystemDirectoryA(sysDir, MAX_PATH);

	char originalPath[MAX_PATH];
	wsprintfA(originalPath, "%s\\dinput8.dll", sysDir);

	g_hOriginalDInput8 = LoadLibraryA(originalPath);
	if (!g_hOriginalDInput8)
		Log("[Error] Failed to load original dinput8.dll");
	else
		Log("[Info] Original dinput8.dll loaded successfully");
}

// ========================================================
// Proxy for DirectInput8Create (main export)
// ========================================================
extern "C" __declspec(dllexport)
HRESULT WINAPI DirectInput8Create(HINSTANCE hInst, DWORD dwVersion, REFIID riid, LPVOID* ppvOut, LPUNKNOWN punkOuter)
{
	LoadOriginalDInput8();

	using DirectInput8Create_t =
		HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);

	auto originalFunc =
		(DirectInput8Create_t)GetProcAddress(g_hOriginalDInput8, "DirectInput8Create");

	return originalFunc(hInst, dwVersion, riid, ppvOut, punkOuter);
}


BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
	if (reason == DLL_PROCESS_ATTACH)
	{
		DisableThreadLibraryCalls(hModule);

		if (!g_CoreStarted.exchange(true))
		{
			CreateThread(nullptr, 0, CoreThread, nullptr, 0, nullptr);
		}
	}
	return TRUE;
}