#include "pch.h"
#include "InGameMessaging.h"

#include <string>
#include <atomic>
#include <cstdint>
#include <Windows.h>

#include "MinHook.h"
#include "Log.h"
#include "Utils.h"

// =============================================================
// Константы для наших кастомных текстов
// =============================================================

// ID строк в FMG, которые мы используем под свои хинты
static constexpr int kCustomSmallHintTextId = 15100001;
static constexpr int kCustomBigHintHeaderId = 15100005;
static constexpr int kCustomBigHintBodyTextId = 15113006;

// Максимальная длина хранимого кастомного текста
static constexpr size_t kMaxHintTextLen = 256;

static wchar_t g_CustomSmallText[256] = L"";
static wchar_t g_CustomBigHeaderText[256] = L"";
static wchar_t g_CustomBigBodyText[256] = L"";

static bool g_HasCustomSmallText = false;
static bool g_HasCustomBigHeaderText = false;
static bool g_HasCustomBigBodyText = false;

static constexpr size_t SMALL_HINT_CAP_CHARS = 256;
static constexpr size_t BIG_HEADER_CAP_CHARS = 256;
static constexpr size_t BIG_BODY_CAP_CHARS = 256;

// =============================================================
// Внутренние типы и функции игры
// =============================================================

struct HintParamsA
{
    int hintId;        // 0x00
    int flags;         // 0x04
    int headerTextId;  // 0x08
    int bodyTextId;    // 0x0C
};

struct HintState
{
    // small
    int32_t smallTextId = -1;
    bool    smallActive = false;

    // big
    int32_t bigHeaderId = -1;
    int32_t bigBodyId = -1;
    bool    bigActive = false;
};
static HintState g_HintState;

// Адреса внутренних функций / указателей в sekiro.exe
// (оставлены те, которые у тебя уже работали)
constexpr uintptr_t HINT_BASEPTR_RVA = 0x00E15E80;  // ShowHintBox_Internal
constexpr uintptr_t SMALL_HINT_FUNC_RVA = 0x00E15EE0;  // ShowSmallHintBoxSimple
constexpr uintptr_t REMOVE_HINTBOX_RVA = 0x00E131F0;  // RemoveHintBox_Internal

// Типы внутренних функций
using ShowSmallHintBoxSimple_t = void(__fastcall*)(HintParamsA* params, int hintId, int textId);
using ShowHintBox_Internal_t = void(__fastcall*)(HintParamsA* paramsA, void* paramsB);
using RemoveHintBox_Internal_t = void(__fastcall*)(int* hintId);

// Глобальные указатели на внутренние функции игры
static ShowSmallHintBoxSimple_t g_ShowSmallHintBoxSimple = nullptr;
static ShowHintBox_Internal_t   g_ShowHintBox_Internal = nullptr;
static RemoveHintBox_Internal_t g_RemoveHintBox_Internal = nullptr;

////////////////////////////////////////////////////////////////

// RVA заглушки ?ActionEventInfo?
constexpr uintptr_t kActionEventInfoRva = 0x2B957F8;

// Сколько wchar-ов мы считаем безопасным писать в этот буфер
constexpr size_t ACTION_STUB_CAPACITY = 64; // можно подправить при желании

void InGameMessaging_WriteActionStubText(const wchar_t* text)
{
    if (!text)
        return;

    // 1. Подготовим временную строку с ограничением по длине
    std::wstring tmp(text);

    // Гарантируем, что не пишем больше, чем помещается в буфер
    if (tmp.size() >= ACTION_STUB_CAPACITY)
    {
        tmp.resize(ACTION_STUB_CAPACITY - 1); // оставляем место под '\0'
    }

    // 2. Получаем базу sekiro.exe
    HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
    if (!hMod)
        return;

    uintptr_t base = reinterpret_cast<uintptr_t>(hMod);
    wchar_t* stubBuf = reinterpret_cast<wchar_t*>(base + kActionEventInfoRva);

    // 3. Снимаем защиту страницы (скорее всего там RX / R)
    SIZE_T bytes = ACTION_STUB_CAPACITY * sizeof(wchar_t);
    DWORD oldProtect = 0;
    if (!VirtualProtect(stubBuf, bytes, PAGE_READWRITE, &oldProtect))
        return;

    // 4. Очищаем буфер и копируем строку
    wmemset(stubBuf, 0, ACTION_STUB_CAPACITY);
    wcsncpy_s(stubBuf, ACTION_STUB_CAPACITY, tmp.c_str(), _TRUNCATE);

    // 5. Возвращаем исходную защиту
    DWORD dummy = 0;
    VirtualProtect(stubBuf, bytes, oldProtect, &dummy);

    // Если хочешь лог:
    // Logf(L"[Hint] Wrote stub text at %p: \"%s\"", stubBuf, tmp.c_str());
}

////////////////////////////////////////////////////////////////

static int  g_BigHintPhase = 0; // 0=ждём header, 1=ждём body
static CRITICAL_SECTION g_HintCs;
static bool g_HintCsInit = false;

static void InitHintCs()
{
    if (!g_HintCsInit)
    {
        InitializeCriticalSection(&g_HintCs);
        g_HintCsInit = true;
    }
}

static size_t ProbeWideBufferLen(wchar_t* buf, size_t maxProbe = 256)
{
    if (!buf)
        return 0;

    __try
    {
#ifdef _MSC_VER
        size_t len = wcsnlen_s(buf, maxProbe);
#else
        size_t len = wcsnlen(buf, maxProbe);
#endif
        if (len == 0)
            return 0;
        return len + 1; // с учётом '\0'
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }
}

static void OverwriteWideBuffer(
    wchar_t* dst,
    const wchar_t* src,
    const char* tagForLog)   // чтобы в логе различать SMALL/BIG HEADER/BIG BODY
{
    if (!dst || !src)
        return;

    size_t bufChars = ProbeWideBufferLen(dst, 256);
    if (bufChars <= 1)
        return;

    size_t srcLen = wcslen(src);
    if (srcLen >= bufChars)
        srcLen = bufChars - 1;

    DWORD oldProtect = 0;
    if (!VirtualProtect(dst, bufChars * sizeof(wchar_t), PAGE_READWRITE, &oldProtect))
        return;

    wmemcpy(dst, src, srcLen);
    dst[srcLen] = L'\0';

    DWORD dummy = 0;
    VirtualProtect(dst, bufChars * sizeof(wchar_t), oldProtect, &dummy);

    Logf("[HintHook] %s overwrite at %p (bufChars=%zu)", tagForLog, dst, bufChars);
}

using HintFunc_t = void(__fastcall*)(void* rcx, void* rdx, void* r8, void* r9);
static HintFunc_t g_HintFunc_Orig = nullptr;

static void __fastcall Hook_HintFunc(void* rcx, void* rdx, void* r8, void* r9)
{
    std::uint8_t* base = reinterpret_cast<std::uint8_t*>(rcx);

    // поля в объекте по RCX
    int32_t flag28 = *reinterpret_cast<int32_t*>(base + 0x28); // для small тут -1
    int32_t smallId = *reinterpret_cast<int32_t*>(base + 0x2C); // id маленького хинта

    int32_t bigBody = *reinterpret_cast<int32_t*>(base + 0x40); // id текста (body) большого хинта
    int32_t bigHeader = *reinterpret_cast<int32_t*>(base + 0x44); // id хедера большого хинта

    bool hasSmall = (smallId != -1);
    bool hasBig = (bigBody != -1 || bigHeader != -1);

    // RESET: хинт исчез, всё в -1
    if (!hasSmall && !hasBig)
    {
        if (g_HintState.smallActive || g_HintState.bigActive)
        {
            Logf("[HintFunc] RESET small=%d bigHeader=%d bigBody=%d",
                g_HintState.smallTextId,
                g_HintState.bigHeaderId,
                g_HintState.bigBodyId);
        }

        g_HintState.smallTextId = -1;
        g_HintState.smallActive = false;

        g_HintState.bigHeaderId = -1;
        g_HintState.bigBodyId = -1;
        g_HintState.bigActive = false;
    }
    // маленький хинт
    else if (hasSmall && !hasBig)
    {
        g_HintState.smallTextId = smallId;
        g_HintState.smallActive = true;

        Logf("[HintFunc] SMALL SHOW smallId=%d (flag28=%d)", smallId, flag28);
    }
    // большой хинт (есть два id в конце)
    else if (hasBig)
    {
        g_HintState.bigBodyId = bigBody;
        g_HintState.bigHeaderId = bigHeader;
        g_HintState.bigActive = true;

        Logf("[HintFunc] BIG SHOW body=%d header=%d", bigBody, bigHeader);
    }

    g_HintFunc_Orig(rcx, rdx, r8, r9);
}


// =============================================================
// Авто-скрытие хинта через 5 секунд
// =============================================================

static volatile LONG g_LastHintTicket = 0;

DWORD WINAPI HintAutoHideThread(LPVOID param)
{
    LONG myTicket = static_cast<LONG>(reinterpret_cast<intptr_t>(param));

    Sleep(5000);

    if (myTicket != g_LastHintTicket)
        return 0;

    // Скрываем наш клиентский хинт (id=0)
    if (g_RemoveHintBox_Internal)
    {
        int id = 0;
        g_RemoveHintBox_Internal(&id);
    }

    return 0;
}

static void StartHintAutoHideTimer()
{
    LONG ticket = InterlockedIncrement(&g_LastHintTicket);

    HANDLE hThread = CreateThread(
        nullptr,
        0,
        HintAutoHideThread,
        reinterpret_cast<LPVOID>(static_cast<intptr_t>(ticket)),
        0,
        nullptr
    );

    if (hThread)
        CloseHandle(hThread);
}

// =============================================================
// Вспомогательные функции установки кастомного текста
// =============================================================

void SetSmallHintText(const wchar_t* text)
{
    if (!text) return; 
    InitHintCs();
    EnterCriticalSection(&g_HintCs);

    wcsncpy_s(g_CustomSmallText, text, _TRUNCATE);
    g_HintState.smallTextId = kCustomSmallHintTextId;  // если нужно, но можно и игнорировать
    g_HintState.smallActive = true;

    LeaveCriticalSection(&g_HintCs);
}

void SetBigHintHeaderText(const wchar_t* text)
{
    if (!text) { g_HasCustomBigHeaderText = false; return; }
    wcsncpy_s(g_CustomBigHeaderText, text, _TRUNCATE);
    g_HasCustomBigHeaderText = true;
}

void SetBigHintBodyText(const wchar_t* text)
{
    if (!text) { g_HasCustomBigBodyText = false; return; }
    wcsncpy_s(g_CustomBigBodyText, text, _TRUNCATE);
    g_HasCustomBigBodyText = true;
}

// =============================================================
// Публичный API (то, чем ты пользуешься из других частей DLL)
// =============================================================

void InGameMessaging_ShowSmallHintBoxSimple(int textId)
{
    if (!g_ShowSmallHintBoxSimple)
        return;

    int hintId = 0;
    HintParamsA params{};
    params.hintId = hintId;
    params.flags = 0;
    params.headerTextId = 0;
    params.bodyTextId = 0;

    g_ShowSmallHintBoxSimple(&params, textId, 0);
    StartHintAutoHideTimer();
}

void InGameMessaging_ShowSmallHintBox_ById(int textId)
{
    // Для простоты используем тот же простой вариант
    InGameMessaging_ShowSmallHintBoxSimple(textId);
}

void InGameMessaging_ShowHintBox_ById(int headerId, int textId)
{
    if (!g_ShowHintBox_Internal)
    {
        Log("[InGameMessaging] ShowHintBox_ById: internal function not ready");
        return;
    }

    HintParamsA params{};
    params.hintId = 0;
    params.flags = 0;
    params.headerTextId = headerId;
    params.bodyTextId = textId;

    // paramsB — это params + 0x08, как мы уже выяснили ранее
    void* paramsB = reinterpret_cast<void*>(
        reinterpret_cast<std::uint8_t*>(&params) + 0x08
        );

    Logf("[InGameMessaging] ShowHintBox_ById: headerId=%d, textId=%d", headerId, textId);

    g_ShowHintBox_Internal(&params, paramsB);

    StartHintAutoHideTimer();
}

void InGameMessaging_RemoveHintBox(int hintId)
{
    if (!g_RemoveHintBox_Internal)
    {
        Log("[InGameMessaging] RemoveHintBox: internal func not ready");
        return;
    }

    int id = hintId;

    Logf("[InGameMessaging] RemoveHintBox(%d)", id);
    g_RemoveHintBox_Internal(&id);
}

void InGameMessaging_RemoveClientHint()
{
    InGameMessaging_RemoveHintBox(0);
}

void InGameMessaging_ShowSmallHint(const wchar_t* text)
{
    if (!text)
        return;

    InGameMessaging_WriteActionStubText(text);
    InGameMessaging_ShowSmallHintBoxSimple(kCustomSmallHintTextId);
}

void InGameMessaging_ShowHint(const wchar_t* text, int headerId = kCustomBigHintHeaderId)
{
    if (!text)
        return;

    InGameMessaging_WriteActionStubText(text);
    InGameMessaging_ShowHintBox_ById(headerId, kCustomSmallHintTextId);
}

// =============================================================
// Помощники для пайпа (UTF-8 -> UTF-16)
// =============================================================

static std::wstring Utf8ToWide(const std::string& s)
{
    if (s.empty())
        return {};

    int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
    if (len <= 0)
        return {};

    std::wstring out(len, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), &out[0], len);
    return out;
}

void InGameMessaging_HandlePipeMessage(const std::string& msg)
{
    if (JsonIsType(msg, "show_hint"))
    {
        std::string text;
        if (!JsonFieldToString(msg, "text", text))
            return;
        
        std::wstring wideText = Utf8ToWide(text);
        if (wideText.empty())
            return;        
        
        int headerId;
		if (!JsonFieldToInt(msg, "header_id", headerId))
            return;

        std::wstring body = FixHintTextFromCSharp(wideText);        
        InGameMessaging_ShowHint(body.c_str(), headerId);
    }
    else if (JsonIsType(msg, "show_small_hint"))
    {
        std::string utf8;
        if (!JsonFieldToString(msg, "text", utf8))
            return;

        std::wstring wide = Utf8ToWide(utf8);
        if (wide.empty())
            return;

        InGameMessaging_ShowSmallHint(wide.c_str());
    }
}

// =============================================================
// Инициализация (хуки + адреса внутренних функций)
// =============================================================

bool InitInGameMessaging()
{
    static bool s_Initialized = false;
    if (s_Initialized)
    {
        Log("[InGameMessaging] already initialized");
        return true;
    }

    // --- MinHook init ---
    {
        MH_STATUS st = MH_Initialize();
        if (st != MH_OK && st != MH_ERROR_ALREADY_INITIALIZED)
        {
            Logf("[InGameMessaging] MH_Initialize failed: %d", (int)st);
            return false;
        }
    }

    HMODULE hMod = GetModuleHandleW(L"sekiro.exe");
    if (!hMod)
    {
        Log("[InGameMessaging] GetModuleHandleW(sekiro.exe) failed");
        return false;
    }

    uintptr_t base = reinterpret_cast<uintptr_t>(hMod);

    // Внутренние функции игры
    g_RemoveHintBox_Internal = reinterpret_cast<RemoveHintBox_Internal_t>(base + REMOVE_HINTBOX_RVA);
    Logf("[InGameMessaging] RemoveHintBox_Internal = %p", g_RemoveHintBox_Internal);

    g_ShowHintBox_Internal = reinterpret_cast<ShowHintBox_Internal_t>(base + HINT_BASEPTR_RVA);
    Logf("[InGameMessaging] ShowHintBox_Internal = %p", g_ShowHintBox_Internal);

    g_ShowSmallHintBoxSimple = reinterpret_cast<ShowSmallHintBoxSimple_t>(base + SMALL_HINT_FUNC_RVA);
    Logf("[InGameMessaging] ShowSmallHintBoxSimple = %p", g_ShowSmallHintBoxSimple);
    
    Log("[InGameMessaging] TextMan::GetText hook installed");

    s_Initialized = true;
    Log("[InGameMessaging] InitInGameMessaging succeeded");
    return true;
}
