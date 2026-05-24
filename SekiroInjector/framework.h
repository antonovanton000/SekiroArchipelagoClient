#pragma once

#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
// Windows Header Files
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <unknwn.h>   // ← ВАЖНО: LPUNKNOWN
#include <d3d11.h>
#include <dxgi.h>
#include <cstdint>
