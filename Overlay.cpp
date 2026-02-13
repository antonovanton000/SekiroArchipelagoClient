// Overlay.cpp
#include "pch.h"
#include "Overlay.h"
#include "Log.h"
#include <vector>
#include <string>
#include <cstdarg>

static HWND  g_hOverlay      = nullptr;
static HWND  g_hGameWindow   = nullptr;
static bool  g_Initialized   = false;

// Simple overlay state
static std::string g_HeaderText = "Sekiro Archipelago Client v1.0.0 (alpha)";
static bool        g_WorldLoaded = false;
static std::vector<std::string> g_LogLines;

// Colors
static const COLORREF OVERLAY_CLEAR_COLOR = RGB(255, 0, 255); // colorkey
static const COLORREF TEXT_COLOR          = RGB(255, 255, 255);

// Forward declarations
static LRESULT CALLBACK Overlay_WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
static void Overlay_RepositionToGame();
static void Overlay_Redraw();

// ---------------------------------------------------------
// Find game window (Sekiro) in current process
// ---------------------------------------------------------

struct FindSekiroWindowData
{
    DWORD targetPid;
    HWND  sekiroHwnd;
    HWND  fallback; // any non-console window of this process
};

static BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam)
{
    auto* data = reinterpret_cast<FindSekiroWindowData*>(lParam);
    if (!data) return TRUE;

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != data->targetPid)
        return TRUE; // not our process

    if (!IsWindowVisible(hwnd))
        return TRUE;

    // Skip console window
    wchar_t className[256] = {};
    if (GetClassNameW(hwnd, className, 256))
    {
        if (wcscmp(className, L"ConsoleWindowClass") == 0)
            return TRUE;
    }

    // Read window title
    wchar_t title[256] = {};
    GetWindowTextW(hwnd, title, 256);

    // Prefer exact "Sekiro" title
    if (wcscmp(title, L"Sekiro") == 0)
    {
        data->sekiroHwnd = hwnd;
        return FALSE; // stop enumeration, we found perfect match
    }

    // Otherwise remember first suitable window as fallback
    if (!data->fallback)
        data->fallback = hwnd;

    return TRUE; // continue enumeration
}

static HWND FindSekiroWindow()
{
    FindSekiroWindowData data{};
    data.targetPid = GetCurrentProcessId();
    data.sekiroHwnd = nullptr;
    data.fallback = nullptr;

    EnumWindows(EnumWindowsProc, reinterpret_cast<LPARAM>(&data));

    if (data.sekiroHwnd)
        return data.sekiroHwnd;

    return data.fallback; // may be null, overlay тогда просто не заведётся
}



// ---------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------

bool Overlay_Init(HWND gameWindowHint)
{
    if (g_Initialized)
        return true;

    // Prefer explicit hint from caller (if any)
    g_hGameWindow = gameWindowHint;
    
    // Если хинта нет — ищем именно окно Sekiro в текущем процессе
    if (!g_hGameWindow)
    {
        g_hGameWindow = FindSekiroWindow();
    }

    if (!g_hGameWindow)
    {
        Logf("[Overlay] Failed to find Sekiro window");
        return false; // не смогли привязаться — выходим из Overlay_Init
    }
    else
    {
        wchar_t title[256] = {};
        GetWindowTextW(g_hGameWindow, title, 256);
        Logf("[Overlay] Attached to window %p (title: %ws)", g_hGameWindow, title);
    }

    // Register overlay window class
    WNDCLASSEXA wc{};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = Overlay_WndProc;
    wc.hInstance     = GetModuleHandle(nullptr);
    wc.lpszClassName = "SekiroApOverlayClass";
    wc.hCursor       = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH); // не критично, всё равно зальём colorkey

    if (!RegisterClassExA(&wc))
    {
        DWORD err = GetLastError();
        if (err != ERROR_CLASS_ALREADY_EXISTS)
        {
            Logf("[Overlay] RegisterClassExA failed: %lu", err);
            return false;
        }
    }

    // Create a layered, topmost, click-through tool window with NO taskbar icon
    g_hOverlay = CreateWindowExA(
        WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW,
        "SekiroApOverlayClass",
        "Sekiro AP Overlay",
        WS_POPUP,          // no border, no caption
        0, 0, 0, 0,
        nullptr,
        nullptr,
        GetModuleHandle(nullptr),
        nullptr
    );

    if (!g_hOverlay)
    {
        Logf("[Overlay] CreateWindowExA failed: %lu", GetLastError());
        return false;
    }

    // Make background color key transparent
    // We'll fill background with OVERLAY_CLEAR_COLOR and key it out.
    if (!SetLayeredWindowAttributes(g_hOverlay, OVERLAY_CLEAR_COLOR, 255, LWA_COLORKEY))
    {
        Logf("[Overlay] SetLayeredWindowAttributes failed: %lu", GetLastError());
    }

    ShowWindow(g_hOverlay, SW_SHOWNOACTIVATE);
    UpdateWindow(g_hOverlay);

    g_Initialized = true;
    Log("[Overlay] Layered overlay initialized");
    return true;
}

void Overlay_Shutdown()
{
    if (g_hOverlay)
    {
        DestroyWindow(g_hOverlay);
        g_hOverlay = nullptr;
    }
    g_Initialized = false;
}

void Overlay_Update()
{
    if (!g_Initialized)
        return;

    // 1) Process overlay messages (so window живёт)
    MSG msg;
    while (PeekMessage(&msg, g_hOverlay, 0, 0, PM_REMOVE))
    {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    // 2) Reposition to game window & redraw
    Overlay_RepositionToGame();
    Overlay_Redraw();
}

void Overlay_SetHeader(const char* text)
{
    if (!text) return;
    g_HeaderText = text;
}

void Overlay_SetWorldLoaded(bool loaded)
{
    g_WorldLoaded = loaded;
}

void Overlay_AddLog(const char* fmt, ...)
{
    char buffer[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);

    g_LogLines.push_back(buffer);

    // keep only last N lines
    const size_t MAX_LINES = 5;
    if (g_LogLines.size() > MAX_LINES)
        g_LogLines.erase(g_LogLines.begin(), g_LogLines.end() - MAX_LINES);
}

// ---------------------------------------------------------------------
// Internals
// ---------------------------------------------------------------------

static void Overlay_RepositionToGame()
{
    if (!g_hOverlay || !g_hGameWindow)
        return;

    RECT gameRect{};
    if (!GetWindowRect(g_hGameWindow, &gameRect))
        return;

    int width  = gameRect.right  - gameRect.left;
    int height = gameRect.bottom - gameRect.top;

    // Нарисуем overlay полоской сверху (например 160 px высотой)
    int overlayHeight = 160;

    SetWindowPos(
        g_hOverlay,
        HWND_TOPMOST,
        gameRect.left,
        gameRect.top,
        width,
        overlayHeight,
        SWP_NOACTIVATE | SWP_SHOWWINDOW
    );
}

static void Overlay_Redraw()
{
    if (!g_hOverlay)
        return;

    RECT rc;
    GetClientRect(g_hOverlay, &rc);

    HDC hdc = GetDC(g_hOverlay);
    if (!hdc) return;

    // Fill background with colorkey color
    HBRUSH hBrush = CreateSolidBrush(OVERLAY_CLEAR_COLOR);
    FillRect(hdc, &rc, hBrush);
    DeleteObject(hBrush);

    SetBkMode(hdc, TRANSPARENT);
    SetTextColor(hdc, TEXT_COLOR);

    int x = 10;
    int y = 30;

    // Header line
    TextOutA(hdc, x, y, g_HeaderText.c_str(), (int)g_HeaderText.size());
    y += 20;

    // Logs
    for (const auto& line : g_LogLines)
    {
        TextOutA(hdc, x, y, line.c_str(), (int)line.size());
        y += 18;
    }

    ReleaseDC(g_hOverlay, hdc);
}

// Very simple window proc (we почти не реагируем на сообщения)
static LRESULT CALLBACK Overlay_WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_DESTROY:
        return 0;
    default:
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
