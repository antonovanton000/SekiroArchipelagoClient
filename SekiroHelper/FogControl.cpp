#include <windows.h>
#include <thread>
#include <fstream>
#include <chrono>

// === Конфигурация ===
static const UINT32 FLAG_ID = 1102999;          // флаг фогвола
static const DWORD64 SETFLAG_RVA = 0x006AAAB0;  // 0x1406AAAB0 - 0x140000000
static const DWORD64 MGR_PTR_RVA = 0x03D55FE8;  // 0x143D55FE8 - 0x140000000

typedef void(__fastcall* TSetEventFlag)(void* mgr, UINT32 flagId, UINT8 value);

// === Лог ===
void Log(const char* msg) {
    std::ofstream("FogControl.log", std::ios::app) << msg << std::endl;
}

// === Получаем указатель на EventFlagMgr ===
void* GetEventFlagMgr() {
    HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
    if (!hBase) return nullptr;
    auto base = reinterpret_cast<uint8_t*>(hBase);
    void** pMgrPtr = reinterpret_cast<void**>(base + MGR_PTR_RVA);
    __try {
        return *pMgrPtr;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return nullptr;
    }
}

// === Получаем функцию SetEventFlag ===
TSetEventFlag GetSetEventFlag() {
    HMODULE hBase = GetModuleHandleW(L"sekiro.exe");
    if (!hBase) return nullptr;
    auto base = reinterpret_cast<uint8_t*>(hBase);
    return reinterpret_cast<TSetEventFlag>(base + SETFLAG_RVA);
}

// === Основная логика ===
DWORD WINAPI MainThread(LPVOID) {
    std::this_thread::sleep_for(std::chrono::seconds(1));

    Log("[FogCtrl] DLL initialized");

    TSetEventFlag SetEventFlag = GetSetEventFlag();
    if (!SetEventFlag) {
        Log("[FogCtrl] ERROR: SetEventFlag not resolved");
        return 0;
    }

    void* mgr = GetEventFlagMgr();
    if (!mgr) {
        Log("[FogCtrl] ERROR: EventFlagMgr not resolved");
        return 0;
    }

    Log("[FogCtrl] OK: resolved pointers");

    // === Включаем стену ===
    Log("[FogCtrl] Turning ON fog wall");
    SetEventFlag(mgr, FLAG_ID, 1);

    std::this_thread::sleep_for(std::chrono::seconds(5));

    // === Выключаем стену ===
    Log("[FogCtrl] Turning OFF fog wall");
    SetEventFlag(mgr, FLAG_ID, 0);

    Log("[FogCtrl] Done, exiting thread");
    return 0;
}

// === Точка входа DLL ===
BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, MainThread, nullptr, 0, nullptr);
    }
    return TRUE;
}
