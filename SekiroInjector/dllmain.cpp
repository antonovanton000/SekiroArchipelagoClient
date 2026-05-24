// dllmain.cpp
#include "pch.h"
#include <windows.h>
#include <thread>
#include <chrono>
#include <atomic>
#include <cstdio>
#include <fstream>
#include <string>
#include <cstring>
#include "SekiroGame.h"
#include <Psapi.h>
#pragma comment(lib, "Psapi.lib")
#include "Log.h"
#include "Core.h"

static HMODULE g_hOriginalDInput8 = nullptr;
static std::atomic<bool> g_CoreStarted = false;
static HANDLE g_CoreStartMutex = nullptr;

static const char* GetBaseName(const char* path)
{
	const char* lastSlash = strrchr(path, '\\');
	const char* lastForwardSlash = strrchr(path, '/');
	const char* last = lastSlash > lastForwardSlash ? lastSlash : lastForwardSlash;
	return last ? last + 1 : path;
}

static bool IsSekiroProcess()
{
	char exePath[MAX_PATH]{};
	if (!GetModuleFileNameA(nullptr, exePath, MAX_PATH))
	{
		Log("[Core] Could not read process image path; allowing startup");
		return true;
	}

	Logf("[Core] Process image: %s", exePath);
	return _stricmp(GetBaseName(exePath), "sekiro.exe") == 0;
}

static bool TryAcquireCoreStartGuard()
{
	char mutexName[96];
	sprintf_s(
		mutexName,
		sizeof(mutexName),
		"Local\\SekiroAPClientCore_%lu",
		GetCurrentProcessId());

	HANDLE mutex = CreateMutexA(nullptr, TRUE, mutexName);
	if (!mutex)
	{
		Log("[Core] CreateMutex guard failed");
		return !g_CoreStarted.exchange(true);
	}

	if (GetLastError() == ERROR_ALREADY_EXISTS)
	{
		Log("[Core] Duplicate core start suppressed by mutex guard");
		CloseHandle(mutex);
		return false;
	}

	g_CoreStartMutex = mutex;
	return true;
}


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

		if (IsSekiroProcess() && !g_CoreStarted.exchange(true) && TryAcquireCoreStartGuard())
		{
			CreateThread(nullptr, 0, CoreThread, nullptr, 0, nullptr);
		}
	}
	return TRUE;
}
