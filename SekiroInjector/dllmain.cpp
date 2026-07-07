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
static HMODULE g_Module = nullptr;
static HMODULE g_ChainDirectInput8Module = nullptr;
static std::atomic<bool> g_CoreStarted = false;
static std::atomic<bool> g_ChainloadStarted = false;
static HANDLE g_CoreStartMutex = nullptr;

using DirectInput8CreateFn = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
static DirectInput8CreateFn g_ChainDirectInput8Create = nullptr;

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

static bool IsAbsolutePath(const std::string& path)
{
	return path.size() >= 3 &&
		((path[1] == ':' && (path[2] == '\\' || path[2] == '/')) ||
		 (path[0] == '\\' && path[1] == '\\'));
}

static std::string Trim(std::string value)
{
	const char* whitespace = " \t\r\n";
	size_t first = value.find_first_not_of(whitespace);
	if (first == std::string::npos)
		return {};

	size_t last = value.find_last_not_of(whitespace);
	return value.substr(first, last - first + 1);
}

static std::string StripInlineComment(std::string value)
{
	bool inQuotes = false;
	for (size_t i = 0; i < value.size(); ++i)
	{
		if (value[i] == '"')
			inQuotes = !inQuotes;
		else if (!inQuotes && (value[i] == '#' || value[i] == ';'))
			return Trim(value.substr(0, i));
	}

	return Trim(value);
}

static std::string GetModuleDirectory(HMODULE module)
{
	char modulePath[MAX_PATH]{};
	if (!GetModuleFileNameA(module, modulePath, MAX_PATH))
		return {};

	char* slash = strrchr(modulePath, '\\');
	if (!slash)
		return {};

	slash[1] = 0;
	return modulePath;
}

static std::string ResolveChainloadPath(const std::string& dllPath, const std::string& moduleDirectory)
{
	if (IsAbsolutePath(dllPath) || moduleDirectory.empty())
		return dllPath;

	return moduleDirectory + dllPath;
}

static std::string GetConfigPath(const std::string& moduleDirectory)
{
	if (moduleDirectory.empty())
		return "injector_config.ini";

	return moduleDirectory + "injector_config.ini";
}

static std::string ReadChainloadPathFromConfig(const std::string& configPath)
{
	char value[1024]{};
	GetPrivateProfileStringA(
		"Chainload",
		"chainDInput8dllPath",
		"",
		value,
		static_cast<DWORD>(sizeof(value)),
		configPath.c_str());

	return StripInlineComment(value);
}

static DWORD WINAPI ChainloadThread(LPVOID parameter)
{
	HMODULE module = reinterpret_cast<HMODULE>(parameter);
	std::string moduleDirectory = GetModuleDirectory(module);
	if (moduleDirectory.empty())
	{
		Log("[Chainload] Could not resolve module directory");
		return 0;
	}

	std::string configPath = GetConfigPath(moduleDirectory);
	std::string entry = ReadChainloadPathFromConfig(configPath);
	if (entry.empty())
	{
		Logf("[Chainload] Disabled; chainDInput8dllPath is empty in %s", configPath.c_str());
		return 0;
	}

	if (entry.front() == '"' && entry.back() == '"' && entry.size() >= 2)
		entry = entry.substr(1, entry.size() - 2);

	std::string resolvedPath = ResolveChainloadPath(entry, moduleDirectory);
	Logf("[Chainload] Reading chainDInput8dllPath from %s", configPath.c_str());

	HMODULE loaded = LoadLibraryA(resolvedPath.c_str());
	if (!loaded)
	{
		Logf("[Chainload] Failed: %s (error %lu)", resolvedPath.c_str(), GetLastError());
		return 0;
	}

	Logf("[Chainload] Loaded: %s", resolvedPath.c_str());

	auto chainDirectInput8Create = reinterpret_cast<DirectInput8CreateFn>(
		GetProcAddress(loaded, "DirectInput8Create"));
	if (chainDirectInput8Create)
	{
		g_ChainDirectInput8Create = chainDirectInput8Create;
		g_ChainDirectInput8Module = loaded;
		Log("[Chainload] Selected DirectInput8Create provider");
	}
	else
	{
		Log("[Chainload] Loaded module does not export DirectInput8Create");
	}

	return 0;
}

static void StartChainload(HMODULE module)
{
	if (g_ChainloadStarted.exchange(true))
		return;

	ChainloadThread(module);
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
	StartChainload(g_Module ? g_Module : reinterpret_cast<HMODULE>(hInst));

	DirectInput8CreateFn createFunc = g_ChainDirectInput8Create;
	if (createFunc)
	{
		char modulePath[MAX_PATH]{};
		if (g_ChainDirectInput8Module && GetModuleFileNameA(g_ChainDirectInput8Module, modulePath, MAX_PATH))
			Logf("[Chainload] Forwarding DirectInput8Create to %s", modulePath);
		else
			Log("[Chainload] Forwarding DirectInput8Create to chainloaded provider");
	}
	else
	{
		LoadOriginalDInput8();
		createFunc = reinterpret_cast<DirectInput8CreateFn>(
			GetProcAddress(g_hOriginalDInput8, "DirectInput8Create"));
	}

	if (!createFunc)
		return E_FAIL;

	return createFunc(hInst, dwVersion, riid, ppvOut, punkOuter);
}


BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
	if (reason == DLL_PROCESS_ATTACH)
	{
		DisableThreadLibraryCalls(hModule);
		g_Module = hModule;

		if (IsSekiroProcess() && !g_CoreStarted.exchange(true) && TryAcquireCoreStartGuard())
		{
			CreateThread(nullptr, 0, CoreThread, nullptr, 0, nullptr);
		}
	}
	return TRUE;
}
