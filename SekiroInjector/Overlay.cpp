// Overlay.cpp
#include "pch.h"
#include "Overlay.h"
#include "Log.h"
#include "Core.h"

#include <vector>
#include <string>
#include <cstdarg>
#include <windows.h>

#include <d3d11.h>
#include <dxgi.h>

#include "MinHook.h"
#include <algorithm>

// Dear ImGui
#include "imgui.h"
#include "imgui_impl_dx11.h"

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")


// Overlay State
static std::string g_HeaderText = "Sekiro Archipelago Client";
static HWND  g_hGameWindow = nullptr;
static bool  g_Initialized = false;

// ---------------------------------------------------------
// DX11 / ImGui state
// ---------------------------------------------------------

static ID3D11Device* g_D3DDevice = nullptr;
static ID3D11DeviceContext* g_D3DContext = nullptr;
static ID3D11RenderTargetView* g_MainRTV = nullptr;

static bool g_ImGuiInitialized = false;

struct LogEntry
{
    std::string text;
    ULONGLONG   timestampMs; // время, когда лог добавлен
};

static std::vector<LogEntry> g_LogLines;

// ---------------------------------------------------------
// MinHook: hook Present / ResizeBuffers
// ---------------------------------------------------------

typedef HRESULT(__stdcall* Present_t)(
    IDXGISwapChain* pSwapChain,
    UINT SyncInterval,
    UINT Flags
    );

typedef HRESULT(__stdcall* ResizeBuffers_t)(
    IDXGISwapChain* pSwapChain,
    UINT BufferCount,
    UINT Width,
    UINT Height,
    DXGI_FORMAT NewFormat,
    UINT SwapChainFlags
    );

static Present_t        g_OrigPresent = nullptr;
static ResizeBuffers_t  g_OrigResizeBuffers = nullptr;

// ---------------------------------------------------------
// Forward-decl
// ---------------------------------------------------------

static HWND     FindSekiroWindow();
static void     Overlay_EnsureD3DResources(IDXGISwapChain* swapChain);
static void     Overlay_ReleaseD3DTargets();
static void     Overlay_ReleaseAll();
static void     Overlay_Draw_ImGui();

// ---------------------------------------------------------
// Helpers
// ---------------------------------------------------------

static std::wstring Utf8ToWide(const std::string& s)
{
    if (s.empty())
        return L"";

    int size = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    if (size <= 0)
        return L"";

    std::wstring w(size, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &w[0], size);
    if (!w.empty() && w.back() == L'\0')
        w.pop_back();
    return w;
}

// ---------------------------------------------------------
// Find Sekiro Window
// ---------------------------------------------------------

struct FindSekiroWindowData
{
    DWORD targetPid;
    HWND  sekiroHwnd;
    HWND  fallback;
};

static BOOL CALLBACK EnumWindowsProc(HWND hwnd, LPARAM lParam)
{
    auto* data = reinterpret_cast<FindSekiroWindowData*>(lParam);
    if (!data) return TRUE;

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != data->targetPid)
        return TRUE;

    if (!IsWindowVisible(hwnd))
        return TRUE;

    // Skip console
    wchar_t className[256] = {};
    if (GetClassNameW(hwnd, className, 256))
    {
        if (wcscmp(className, L"ConsoleWindowClass") == 0)
            return TRUE;
    }

    wchar_t title[256] = {};
    GetWindowTextW(hwnd, title, 256);

    if (wcscmp(title, L"Sekiro") == 0)
    {
        data->sekiroHwnd = hwnd;
        return FALSE;
    }

    if (!data->fallback)
        data->fallback = hwnd;

    return TRUE;
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

    return data.fallback;
}

// ---------------------------------------------------------
//  DXGI swap chain
// ---------------------------------------------------------

static HRESULT __stdcall Hooked_Present(
    IDXGISwapChain* pSwapChain,
    UINT SyncInterval,
    UINT Flags)
{
    Overlay_EnsureD3DResources(pSwapChain);

    if (g_D3DDevice && g_D3DContext && g_MainRTV && g_ImGuiInitialized)
    {
        g_D3DContext->OMSetRenderTargets(1, &g_MainRTV, nullptr);

        DXGI_SWAP_CHAIN_DESC desc{};
        if (SUCCEEDED(pSwapChain->GetDesc(&desc)))
        {
            D3D11_VIEWPORT vp{};
            vp.TopLeftX = 0.0f;
            vp.TopLeftY = 0.0f;
            vp.Width = static_cast<FLOAT>(desc.BufferDesc.Width);
            vp.Height = static_cast<FLOAT>(desc.BufferDesc.Height);
            vp.MinDepth = 0.0f;
            vp.MaxDepth = 1.0f;
            g_D3DContext->RSSetViewports(1, &vp);

            ImGuiIO& io = ImGui::GetIO();
            io.DisplaySize = ImVec2(vp.Width, vp.Height);
        }

        ImGui_ImplDX11_NewFrame();
        ImGui::NewFrame();

        Overlay_Draw_ImGui();

        ImGui::Render();
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    }

    return g_OrigPresent(pSwapChain, SyncInterval, Flags);
}

static HRESULT __stdcall Hooked_ResizeBuffers(
    IDXGISwapChain* pSwapChain,
    UINT BufferCount,
    UINT Width,
    UINT Height,
    DXGI_FORMAT NewFormat,
    UINT SwapChainFlags)
{
    Overlay_ReleaseD3DTargets();

    return g_OrigResizeBuffers(
        pSwapChain,
        BufferCount,
        Width,
        Height,
        NewFormat,
        SwapChainFlags
    );
}

// ---------------------------------------------------------
// Public API 
// ---------------------------------------------------------

bool Overlay_Init(HWND gameWindowHint)
{
    if (g_Initialized)
        return true;

    g_hGameWindow = gameWindowHint;
    if (!g_hGameWindow)
        g_hGameWindow = FindSekiroWindow();

    if (!g_hGameWindow)
    {
        Logf("[Overlay] Failed to find Sekiro window");
        return false;
    }
    else
    {
        wchar_t title[256] = {};
        GetWindowTextW(g_hGameWindow, title, 256);
        Logf("[Overlay] Attached to window %p (title: %ws)", g_hGameWindow, title);
    }

    // MinHook init
    MH_STATUS mhStatus = MH_Initialize();
    if (mhStatus != MH_OK && mhStatus != MH_ERROR_ALREADY_INITIALIZED)
    {
        Logf("[Overlay] MH_Initialize failed: %d", (int)mhStatus);
        return false;
    }

    // Creating temp device + swapchain, just to get vtable
    DXGI_SWAP_CHAIN_DESC scd{};
    scd.BufferCount = 1;
    scd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    scd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    scd.OutputWindow = g_hGameWindow;
    scd.SampleDesc.Count = 1;
    scd.SampleDesc.Quality = 0;
    scd.Windowed = TRUE;
    scd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    D3D_FEATURE_LEVEL featureLevels[] =
    {
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };
    D3D_FEATURE_LEVEL featureLevelOut = D3D_FEATURE_LEVEL_11_0;

    IDXGISwapChain* dummySwapChain = nullptr;
    ID3D11Device* dummyDevice = nullptr;
    ID3D11DeviceContext* dummyContext = nullptr;

    HRESULT hr = D3D11CreateDeviceAndSwapChain(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        0,
        featureLevels,
        _countof(featureLevels),
        D3D11_SDK_VERSION,
        &scd,
        &dummySwapChain,
        &dummyDevice,
        &featureLevelOut,
        &dummyContext
    );

    if (FAILED(hr))
    {
        Logf("[Overlay] D3D11CreateDeviceAndSwapChain failed: 0x%08X", hr);
        return false;
    }

    void** vtbl = *reinterpret_cast<void***>(dummySwapChain);
    void* presentAddr = vtbl[8];   // IDXGISwapChain::Present
    void* resizeBuffersAddr = vtbl[13];  // IDXGISwapChain::ResizeBuffers

    dummySwapChain->Release();
    dummyDevice->Release();
    dummyContext->Release();

    mhStatus = MH_CreateHook(presentAddr, &Hooked_Present, reinterpret_cast<void**>(&g_OrigPresent));
    if (mhStatus != MH_OK && mhStatus != MH_ERROR_ALREADY_CREATED)
    {
        Logf("[Overlay] MH_CreateHook(Present) failed: %d", (int)mhStatus);
        return false;
    }

    mhStatus = MH_EnableHook(presentAddr);
    if (mhStatus != MH_OK && mhStatus != MH_ERROR_ENABLED)
    {
        Logf("[Overlay] MH_EnableHook(Present) failed: %d", (int)mhStatus);
        return false;
    }

    mhStatus = MH_CreateHook(resizeBuffersAddr, &Hooked_ResizeBuffers, reinterpret_cast<void**>(&g_OrigResizeBuffers));
    if (mhStatus != MH_OK && mhStatus != MH_ERROR_ALREADY_CREATED)
    {
        Logf("[Overlay] MH_CreateHook(ResizeBuffers) failed: %d", (int)mhStatus);
    }
    else
    {
        mhStatus = MH_EnableHook(resizeBuffersAddr);
        if (mhStatus != MH_OK && mhStatus != MH_ERROR_ENABLED)
        {
            Logf("[Overlay] MH_EnableHook(ResizeBuffers) failed: %d", (int)mhStatus);
        }
    }

    g_Initialized = true;
    Log("[Overlay] In-game DX11 ImGui overlay initialized");
    return true;
}

void Overlay_Shutdown()
{
    if (!g_Initialized)
        return;

    Overlay_ReleaseAll();

    MH_Uninitialize(); 
    g_Initialized = false;
    Log("[Overlay] Overlay shutdown");
}

void Overlay_SetHeader(const char* text)
{
    if (!text) return;
    g_HeaderText = text;
}

void Overlay_AddLog(const char* fmt, ...)
{
    char buffer[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);

    LogEntry entry;
    entry.text = buffer;
    entry.timestampMs = GetTickCount64();

    g_LogLines.push_back(std::move(entry));    
    const size_t MAX_LINES = 50;
    if (g_LogLines.size() > MAX_LINES)
        g_LogLines.erase(g_LogLines.begin(), g_LogLines.begin() + (g_LogLines.size() - MAX_LINES));
}

// ---------------------------------------------------------
//  D3D / ImGui Initialization
// ---------------------------------------------------------

static void Overlay_EnsureD3DResources(IDXGISwapChain* swapChain)
{
    if (!swapChain)
        return;

    if (!g_D3DDevice || !g_D3DContext)
    {
        HRESULT hr = swapChain->GetDevice(__uuidof(ID3D11Device), (void**)&g_D3DDevice);
        if (FAILED(hr) || !g_D3DDevice)
        {
            Logf("[Overlay] GetDevice failed: 0x%08X", hr);
            return;
        }

        g_D3DDevice->GetImmediateContext(&g_D3DContext);
        Log("[Overlay] D3D device & context captured");
    }

    // RTV
    if (!g_MainRTV)
    {
        ID3D11Texture2D* backBuffer = nullptr;
        HRESULT hr = swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&backBuffer);
        if (FAILED(hr) || !backBuffer)
        {
            Logf("[Overlay] GetBuffer failed: 0x%08X", hr);
            return;
        }

        hr = g_D3DDevice->CreateRenderTargetView(backBuffer, nullptr, &g_MainRTV);
        backBuffer->Release();

        if (FAILED(hr))
        {
            Logf("[Overlay] CreateRenderTargetView failed: 0x%08X", hr);
            return;
        }
    }

    // ImGui init
    if (!g_ImGuiInitialized && g_D3DDevice && g_D3DContext)
    {
        IMGUI_CHECKVERSION();
        ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO();
        io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags_NoMouseCursorChange;
        
        ImGui::StyleColorsDark();

        if (!ImGui_ImplDX11_Init(g_D3DDevice, g_D3DContext))
        {
            Log("[Overlay] ImGui_ImplDX11_Init failed");
        }
        else
        {
            g_ImGuiInitialized = true;
            Log("[Overlay] ImGui DX11 initialized");
        }
    }
}

static void Overlay_ReleaseD3DTargets()
{
    if (g_MainRTV) { g_MainRTV->Release(); g_MainRTV = nullptr; }
}

static void Overlay_ReleaseAll()
{
    if (g_ImGuiInitialized)
    {
        ImGui_ImplDX11_Shutdown();
        ImGui::DestroyContext();
        g_ImGuiInitialized = false;
    }

    Overlay_ReleaseD3DTargets();

    if (g_D3DContext) { g_D3DContext->Release(); g_D3DContext = nullptr; }
    if (g_D3DDevice) { g_D3DDevice->Release();  g_D3DDevice = nullptr; }
}

// ---------------------------------------------------------
// Draw UI using ImGui
// ---------------------------------------------------------

static void Overlay_Draw_ImGui()
{
    if (!g_IsDebug)
        return;

    // --- 1) Clear ---
    const ULONGLONG now = GetTickCount64();
    const ULONGLONG lifetimeMs = 10000; // 10 sec
    g_LogLines.erase(
        std::remove_if(
            g_LogLines.begin(),
            g_LogLines.end(),
            [now, lifetimeMs](const LogEntry& e)
            {
                return (now - e.timestampMs) > lifetimeMs;
            }),
        g_LogLines.end()
    );

    // --- 2) Set up window ImGui ---
    ImGuiWindowFlags flags =
        ImGuiWindowFlags_NoTitleBar |
        ImGuiWindowFlags_NoResize |
        ImGuiWindowFlags_NoMove |
        ImGuiWindowFlags_NoSavedSettings |
        ImGuiWindowFlags_NoScrollbar |       
        ImGuiWindowFlags_NoInputs |          
        ImGuiWindowFlags_AlwaysAutoResize;   

    // Position in top left corner
    ImGui::SetNextWindowPos(ImVec2(10.0f, 10.0f), ImGuiCond_Always);
    ImGui::SetNextWindowBgAlpha(0.5f);

    ImGui::Begin("SekiroApOverlay", nullptr, flags);

    // --- 3) Header  ---
    ImGui::TextUnformatted(g_HeaderText.c_str());

    // --- 4) Logs ---
    if (!g_LogLines.empty())
    {
        ImGui::Separator();

        for (const auto& entry : g_LogLines)
        {
            ImGui::TextUnformatted(entry.text.c_str());
        }
    }

    ImGui::End();
}

