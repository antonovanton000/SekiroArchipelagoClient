#include "pch.h"
#include "Dx11Hook.h"
#include "Log.h"
#include <MinHook.h>
#include <dxgi.h>
#include "OverlayState.h"
#include "imgui.h"
#include "backends/imgui_impl_dx11.h"
#include "backends/imgui_impl_win32.h"
#include <d3d11.h>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")


typedef HRESULT(__stdcall* PresentFn)(
    IDXGISwapChain* swapChain,
    UINT syncInterval,
    UINT flags
    );

static PresentFn oPresent = nullptr;
static bool g_ImGuiInitialized = false;
static ID3D11Device* g_Device = nullptr;
static ID3D11DeviceContext* g_Context = nullptr;
static HWND g_hWnd = nullptr;

HRESULT __stdcall hkPresent(
    IDXGISwapChain* swapChain,
    UINT syncInterval,
    UINT flags
)
{
    if (!g_ImGuiInitialized)
    {
        if (SUCCEEDED(swapChain->GetDevice(
            __uuidof(ID3D11Device),
            (void**)&g_Device)))
        {
            g_Device->GetImmediateContext(&g_Context);

            DXGI_SWAP_CHAIN_DESC desc{};
            swapChain->GetDesc(&desc);
            g_hWnd = desc.OutputWindow;

            ImGui::CreateContext();
            ImGui_ImplWin32_Init(g_hWnd);
            ImGui_ImplDX11_Init(g_Device, g_Context);

            g_ImGuiInitialized = true;
            Log("[Overlay] DX11 + SwapChain acquired");
        }
    }

    if (!g_ImGuiInitialized)
    {
        // Если по какой-то причине не инициализировались — просто даём игре рисовать дальше
        return oPresent(swapChain, syncInterval, flags);
    }

    ImGui_ImplDX11_NewFrame();
    ImGui_ImplWin32_NewFrame();
    ImGui::NewFrame();

    ImGui::Begin("Sekiro AP Debug", nullptr,
        ImGuiWindowFlags_NoCollapse |
        ImGuiWindowFlags_AlwaysAutoResize);

    ImGui::Text("Sekiro AP Client");
    ImGui::Separator();
    ImGui::Text("WorldLoaded: %s",
        g_OverlayState.worldLoaded ? "YES" : "NO");

    ImGui::End();

    ImGui::Render();
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    return oPresent(swapChain, syncInterval, flags);
}


void InitDx11Hook()
{
    Log("[Overlay] Initializing DX11 hook");

    // Create a dummy device to grab Present vtable
    DXGI_SWAP_CHAIN_DESC sd{};
    sd.BufferCount = 1;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = GetForegroundWindow();
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;

    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    IDXGISwapChain* swapChain = nullptr;

    if (D3D11CreateDeviceAndSwapChain(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        0,
        nullptr,
        0,
        D3D11_SDK_VERSION,
        &sd,
        &swapChain,
        &device,
        nullptr,
        &context) != S_OK)
    {
        Log("[Overlay] Failed to create dummy D3D11 device");
        return;
    }

    void** vtable = *reinterpret_cast<void***>(swapChain);
    oPresent = (PresentFn)vtable[8];

    MH_CreateHook(
        oPresent,
        hkPresent,
        reinterpret_cast<void**>(&oPresent)
    );
    MH_EnableHook(oPresent);

    swapChain->Release();
    device->Release();
    context->Release();

    Log("[Overlay] Present hook installed");
}

