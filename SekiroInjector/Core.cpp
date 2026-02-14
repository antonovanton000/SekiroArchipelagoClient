// Core.cpp
#include "pch.h"
#include "Core.h"
#include "Log.h"
#include "SekiroGame.h"
#include <Windows.h>
#include <atomic>
#include "Overlay.h"
#include "ItemHooks.h"
#include "PipeConnection.h"
#include "Utils.h"
#include "InGameMessaging.h"

static bool g_Running = true;
static bool g_HooksInitialized = false;
bool g_IsDebug = false;

PipeConnection g_Pipe;

static void HandlePipeMessage(const std::string& msg)
{
    Logf("[Pipe] Incoming: %s", msg.c_str());
    if (JsonIsType(msg, "debug_state"))
    {
        bool newDebug;
        if (!JsonFieldToBool(msg, "value", newDebug))
        {
            Log("[Pipe] debug_state: missing or invalid value");
            return;
        }
        g_IsDebug = newDebug;
    }

    if (JsonIsType(msg, "kill_player"))
    {
        SekiroGame_KillPlayer();
        Logf("[Pipe] Kill Player");
        Overlay_AddLog("[Pipe] Kill Player");
    }
    
    // --- handle grant_item messages ---   
    if (JsonIsType(msg, "grant_item"))
    {
        uint32_t goodsId = 0;
        uint32_t quantity = 1;
        uint32_t eventId = 0;

        // goods_id is required
        if (!JsonFieldToUInt(msg, "goods_id", goodsId))
        {
            Log("[Pipe] grant_item: missing or invalid goods_id");
            return;
        }

        // quantity is optional, defaults to 1, clamp 0 → 1
        if (!JsonFieldToUInt(msg, "quantity", quantity) || quantity == 0)
        {
            quantity = 1;
        }

        // event_id is optional; if not present or invalid, stays 0
        JsonFieldToUInt(msg, "event_id", eventId);

        Logf("[Pipe] grant_item -> goodsId=%u qty=%u eventId=%u",
            goodsId, quantity, eventId);
        
         if (!IsWorldLoaded())
         {
             Log("[Pipe] grant_item: world is not loaded, skipping for now");
             return;
         }

        if (eventId != 0)
        {
            // New path: set event flag first, then grant the item.
            SekiroGame_GrantItemWithEvent(eventId, goodsId, quantity);
        }
        else
        {
            // Backwards-compatible path: old behavior without event flag.
            PendingApItem item{};
            item.itemId = goodsId;
            item.quantity = quantity;

            SekiroGame_GrantItem(item);
        }

        return;
    }
    if (JsonIsType(msg, "show_hint") || JsonIsType(msg, "show_small_hint"))
    {        
        InGameMessaging_HandlePipeMessage(msg);
    }
    if (JsonIsType(msg, "show_hint_by_id"))
    {
        uint32_t headerId = 0;
        uint32_t textId = 0;
        if (!JsonFieldToUInt(msg, "headerId", headerId))
        {
            Log("[Pipe] show_hint_by_id: missing or invalid headerId");
            return;
        }
        if (!JsonFieldToUInt(msg, "textId", textId))
        {
            Log("[Pipe] show_hint_by_id: missing or invalid textId");
            return;
        }
		InGameMessaging_ShowHintBox_ById(static_cast<int>(headerId), static_cast<int>(textId));
    }
    if (JsonIsType(msg, "show_small_hint_by_id"))
    {
        uint32_t msgId = 0;
        if (!JsonFieldToUInt(msg, "msgId", msgId))
        {
            Log("[Pipe] show_small_hint_by_id: missing or invalid msgId");
            return;
        }
        InGameMessaging_ShowSmallHintBox_ById(static_cast<int>(msgId));
    }
}

DWORD WINAPI CoreThread(LPVOID)
{
    Logf("[Core] Thread started");

    if (!SekiroGame_Initialize())
    {
        Logf("[Core] Initialization failed");
        return 0;
    }
    
    g_Pipe.Initialize(L"\\\\.\\pipe\\SekiroAP");

    if (!g_HooksInitialized)
    {
        if (ItemHooks_Initialize())
        {
            g_HooksInitialized = true;
            Log("[Core] ItemHooks initialized successfully");
        }
        else
        {
            Log("[Core] ItemHooks_Initialize failed");
        }
    }

    Logf("[Core] Initialization OK");

    // Init overlay as early as possible — ещё в меню игры.
    Overlay_SetHeader("Sekiro Archipelago Client (debug)");
    if (!Overlay_Init(nullptr))
    {
        Log("[Core] Overlay_Init failed, overlay disabled");
    }

    while (g_Running)
    {
        // Game logic
        SekiroGame_Update();

		// Pipe communication (reconnect, send outgoing, receive incoming)
        g_Pipe.Tick();
        
        std::string msg;
        while (g_Pipe.PopIncoming(msg))
        {
			HandlePipeMessage(msg);
        }

        Sleep(33); // ~30 fps for overlay
    }

    Overlay_Shutdown();

    Logf("[Core] Thread stopped");
    return 0;
}
