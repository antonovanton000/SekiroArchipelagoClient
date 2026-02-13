#include <windows.h>
#include <atomic>
#include <fstream>
#include <string>
#include <thread>
#include <chrono>
#include <Psapi.h>
#pragma comment(lib, "Psapi.lib")

// === Конфигурация ===
static const UINT32 FLAG_ID = 1102999;
static const DWORD64 SETFLAG_RVA = 0x006AAAB0;   // 0x1406AAAB0 - 0x140000000
static const DWORD64 MGR_PTR_RVA = 0x03D55FE8;   // 0x143D55FE8 - 0x140000000
//static const wchar_t* CONTROL_FILE = L"bingomod\\fogwall.cfg";
static const wchar_t* CONTROL_FILE = L"huntpointmod\\fogwall.cfg";

// === Тип функции ===
typedef void(__fastcall* TSetEventFlag)(void* mgr, UINT32 flagId, UINT8 value);

static HMODULE g_hOriginalDInput8 = nullptr;
static std::atomic<bool> g_running{ false };
static HANDLE g_thread = nullptr;

static bool ReadFogWallState(bool& outState)
{
    std::wifstream f(CONTROL_FILE);
    if (!f.is_open()) return false;

    std::wstring line;
    std::getline(f, line);
    f.close();

    if (line.find(L"ON") != std::wstring::npos)
    {
        outState = true;
        return true;
    }
    if (line.find(L"OFF") != std::wstring::npos)
    {
        outState = false;
        return true;
    }
    return false;
}

static void* ResolveEventFlagMgr()
{
    HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
    if (!hBase) return nullptr;
    auto base = reinterpret_cast<uint8_t*>(hBase);
    void** pMgr = reinterpret_cast<void**>(base + MGR_PTR_RVA);
    __try {
        return *pMgr;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}

static TSetEventFlag ResolveSetEventFlag()
{
    HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
    if (!hBase) return nullptr;
    return reinterpret_cast<TSetEventFlag>(
        reinterpret_cast<uint8_t*>(hBase) + SETFLAG_RVA
        );
}



// ========================================================
// Логирование через WinAPI (без CRT)
// ========================================================
void LogMessage(const char* msg)
{
    /*HANDLE hFile = CreateFileA(
        "output.txt",
        FILE_APPEND_DATA,
        FILE_SHARE_READ,
        nullptr,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr
    );

    if (hFile == INVALID_HANDLE_VALUE)
        return;

    DWORD written = 0;
    WriteFile(hFile, msg, (DWORD)lstrlenA(msg), &written, nullptr);
    WriteFile(hFile, "\r\n", 2, &written, nullptr);
    CloseHandle(hFile);*/
}

void LogMessagef(const char* fmt, ...)
{
    char buf[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    LogMessage(buf);
}

// ========== Безопасное чтение из памяти ==========
uintptr_t FindWorldChrManPtr()
{
    HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
    if (!hMod) return 0;

    uintptr_t base = reinterpret_cast<uintptr_t>(hMod);
    MODULEINFO info{};
    GetModuleInformation(GetCurrentProcess(), hMod, &info, sizeof(info));
    uintptr_t size = (uintptr_t)info.SizeOfImage;

    const BYTE pattern[] = { 0x48, 0x8B, 0x35, 0x00, 0x00, 0x00, 0x00, 0x44, 0x0F, 0x28, 0x18 };
    const char mask[] = "xxx????xxxx";

    for (uintptr_t i = base; i < base + size - sizeof(pattern); i++)
    {
        bool found = true;
        for (size_t j = 0; j < sizeof(pattern); j++)
        {
            if (mask[j] != '?' && ((BYTE*)i)[j] != pattern[j])
            {
                found = false;
                break;
            }
        }
        if (found)
        {
            int rel = *(int*)(i + 3);
            uintptr_t worldChrManAddr = i + 7 + rel;
            LogMessagef("[FindWorldChrManPtr] Found at 0x%llX (ptr=0x%llX)", i, worldChrManAddr);
            return worldChrManAddr;
        }
    }

    LogMessage("[FindWorldChrManPtr] Pattern not found");
    return 0;
}

// адрес переменной, где хранится pointer на WorldChrMan
static uintptr_t g_WCMStorageAddr = 0; // это то, что возвращает FindWorldChrManPtr()

static inline bool SafeReadPtr(uintptr_t addr, uintptr_t& out) {
    if (!addr) return false;
    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;
    const DWORD ok = PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE;
    if (!(mbi.Protect & ok)) return false;
    __try { out = *(uintptr_t*)addr; return out != 0; }
    __except (EXCEPTION_EXECUTE_HANDLER) { out = 0; return false; }
}

static inline bool SafeReadInt(uintptr_t addr, int& out) {
    if (!addr) return false;
    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;
    const DWORD ok = PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE;
    if (!(mbi.Protect & ok)) return false;
    __try { out = *(int*)addr; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { out = 0; return false; }
}


bool IsWorldLoaded() {
    // 1) лениво находим “хранилище” указателя на WorldChrMan
    if (!g_WCMStorageAddr) {
        g_WCMStorageAddr = FindWorldChrManPtr(); // лог у тебя уже есть
        if (!g_WCMStorageAddr) {
            //LogMessage("[IsWorldLoaded] Failed to find WorldChrMan pattern");
            return false;
        }
    }

    // 2) читаем ОДИН раз сам pointer на WorldChrMan
    uintptr_t worldChrMan = 0;
    if (!SafeReadPtr(g_WCMStorageAddr, worldChrMan) || !worldChrMan) {
        // во время меню это нормально – переменная ещё нулевая
        //LogMessage("[IsWorldLoaded] worldChrMan (read from storage) invalid");
        return false;
    }

    // 3) дальше стандартная цепочка как в твоей CT/Lua
    uintptr_t p1 = 0, p2 = 0, p3 = 0;
    if (!SafeReadPtr(worldChrMan + 0x88, p1) || !p1) { 
        //LogMessage("[IsWorldLoaded] p1 (+0x88) invalid");  
        return false; 
    }
    if (!SafeReadPtr(p1 + 0x1FF8, p2) || !p2) { 
        //LogMessage("[IsWorldLoaded] p2 (+0x1FF8) invalid"); 
        return false; 
    }
    if (!SafeReadPtr(p2 + 0x18, p3) || !p3) { 
        //LogMessage("[IsWorldLoaded] p3 (+0x18) invalid");   
        return false; 
    }

    int hp = 0;
    if (!SafeReadInt(p3 + 0x130, hp)) { 
        //LogMessage("[IsWorldLoaded] hp read failed"); 
        return false; 
    }
    if (hp <= 0 || hp > 9999) { 
        //LogMessagef("[IsWorldLoaded] hp=%d (range invalid)", hp); 
        return false; 
    }

    //LogMessagef("[IsWorldLoaded] OK, hp=%d", hp);
    return true;
}


// ========================================================
// Твой рабочий поток
// ========================================================
DWORD WINAPI WorkerThread(LPVOID)
{
    LogMessage("[FogCtrl] Worker started");

    using SetEventFlag_t = void(*)(uintptr_t, int, bool);
    SetEventFlag_t SetEventFlag = (SetEventFlag_t)0x1406AAAB0;

    uintptr_t mgrPtrAddr = 0x143D55FE8; // твой найденный указатель
    uintptr_t mgr = 0;
    bool currentState = false;

    auto ReadFogSetting = []() -> bool {
        //std::ifstream f("bingomod\\fogwall.cfg");
        std::ifstream f("huntpointmod\\fogwall.cfg");
        if (!f.is_open()) return false;
        std::string line;
        std::getline(f, line);
        return line.find("FogWall=ON") != std::string::npos;
        };

    // Основной цикл
    while (g_running.load())
    {
        // --- 1. Дождаться загрузки игрока ---
        if (!IsWorldLoaded())
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(1000));    
			//LogMessage("[FogCtrl] Waiting for world load...");
            continue;
        }

        // --- 2. Получить EventFlagMgr ---
        if (!mgr)
        {
            uintptr_t first = 0;
            __try { first = *(uintptr_t*)mgrPtrAddr; }
            __except (EXCEPTION_EXECUTE_HANDLER) { first = 0; }

            if (first)
            {
                __try { mgr = *(uintptr_t*)first; }
                __except (EXCEPTION_EXECUTE_HANDLER) { mgr = 0; }

                if (mgr)
                {
                    char buf[128];
                    sprintf_s(buf, "[FogCtrl] EventFlagMgr resolved = 0x%p", (void*)mgr);
                    LogMessage(buf);
                }
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
            continue;
        }

        // --- 3. Проверить и применить состояние ---
        bool newState = ReadFogSetting();
        if (newState != currentState)
        {
            currentState = newState;
            __try {
                SetEventFlag(mgr, 1102999, currentState);
                LogMessage(currentState
                    ? "[FogCtrl] FogWall -> ON"
                    : "[FogCtrl] FogWall -> OFF");
            }
            __except (EXCEPTION_EXECUTE_HANDLER) {
                LogMessage("[FogCtrl] Exception in SetEventFlag, skipping...");
            }
        }

        std::this_thread::sleep_for(std::chrono::seconds(2));
    }

    LogMessage("[FogCtrl] Worker stopped");
    return 0;
}






// ========================================================
// Помощник: загрузить оригинальную dinput8.dll
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
        LogMessage("[Error] Failed to load original dinput8.dll");
    else
        LogMessage("[Info] Original dinput8.dll loaded successfully");
}

// ========================================================
// Прокси для DirectInput8Create (главный экспорт)
// ========================================================
extern "C" __declspec(dllexport)
HRESULT WINAPI DirectInput8Create(HINSTANCE hInst, DWORD dwVersion, REFIID riid, LPVOID* ppvOut, LPUNKNOWN punkOuter)
{
    LoadOriginalDInput8();

    if (!g_hOriginalDInput8)
        return E_FAIL;

    using DirectInput8Create_t = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, LPUNKNOWN);
    auto originalFunc = (DirectInput8Create_t)GetProcAddress(g_hOriginalDInput8, "DirectInput8Create");

    if (!originalFunc)
        return E_FAIL;

    LogMessage("[Proxy] DirectInput8Create called — forwarding to original");
    return originalFunc(hInst, dwVersion, riid, ppvOut, punkOuter);
}

// ========================================================
// DLL entry point
// ========================================================
BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        LogMessage("[Attach] DInput8Proxy loaded into process");

        LoadOriginalDInput8();

        g_running.store(true);
        g_thread = CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
        break;

    case DLL_PROCESS_DETACH:
        g_running.store(false);
        if (g_thread) {
            WaitForSingleObject(g_thread, 1000);
            CloseHandle(g_thread);
            g_thread = nullptr;
        }
        // === Новое: при выгрузке отключаем фогволл ===
        {
            FILE* f = nullptr;            
            //fopen_s(&f, "bingomod\\fogwall.cfg", "w");
            fopen_s(&f, "huntpointmod\\fogwall.cfg", "w");
            if (f)
            {
                fprintf(f, "FogWall=OFF\n");
                fclose(f);
                LogMessage("[FogCtrl] FogWall=OFF written to fogwall.txt");
            }
            else
            {
                LogMessage("[FogCtrl] ERROR: Failed to open fogwall.txt for writing on detach");
            }
        }

        LogMessage("[Detach] DInput8Proxy unloaded");
        if (g_hOriginalDInput8)
            FreeLibrary(g_hOriginalDInput8);
        break;
    }
    return TRUE;
}