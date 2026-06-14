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
bool g_IsFullDeathDetection = true;

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
    if (JsonIsType(msg, "full_death_detection"))
    {
        bool newDeathDetection;
        if (!JsonFieldToBool(msg, "value", newDeathDetection))
        {
            Logf("[Pipe] full_death_detection: missing or invalid value");
            return;
        }
        g_IsFullDeathDetection = newDeathDetection;
        Logf("[Pipe] full_death_detection: %s", g_IsFullDeathDetection ? "true" : "false");
        Overlay_AddLog("[Pipe] full_death_detection: %s", g_IsFullDeathDetection ? "true" : "false");
    }

    if (JsonIsType(msg, "kill_player"))
    {
        SekiroGame_KillPlayer();
        Logf("[Pipe] Kill Player");
        Overlay_AddLog("[Pipe] Kill Player");
    }

    if (JsonIsType(msg, "set_enemy_ai_disabled"))
    {
        bool disabled;
        if (!JsonFieldToBool(msg, "value", disabled))
        {
            Log("[Pipe] set_enemy_ai_disabled: missing or invalid value");
            return;
        }

        bool ok = SekiroGame_SetEnemyAiDisabled(disabled);
        Logf("[Pipe] set_enemy_ai_disabled: %s (%s)",
            disabled ? "true" : "false",
            ok ? "ok" : "failed");
        Overlay_AddLog("[Pipe] Enemy AI disabled: %s", disabled ? "true" : "false");
    }

    if (JsonIsType(msg, "set_one_hit_kill"))
    {
        bool enabled;
        if (!JsonFieldToBool(msg, "value", enabled))
        {
            Log("[Pipe] set_one_hit_kill: missing or invalid value");
            return;
        }

        bool ok = SekiroGame_SetOneHitKillEnabled(enabled);
        Logf("[Pipe] set_one_hit_kill: %s (%s)",
            enabled ? "true" : "false",
            ok ? "ok" : "failed");
        Overlay_AddLog("[Pipe] One Hit Kill: %s", enabled ? "true" : "false");
    }
    
    if (JsonIsType(msg, "set_event_flag"))
    {     
        uint32_t eventId = 0;
        uint32_t value = 0;

        // event_id is required
        if (!JsonFieldToUInt(msg, "event_id", eventId))
        {
            Log("[Pipe] set_event_flag: missing or invalid event_id");
            return;
        }

        // value is required
        if (!JsonFieldToUInt(msg, "value", value))
        {
            Log("[Pipe] set_event_flag: missing or invalid value");
            return;
        }
        Logf("[Pipe] set_event_flag: event_flag_id: %u, value: %u", eventId, value);

		SetEventFlagSafe(eventId, value == 1);
    }

    if (JsonIsType(msg, "get_event_flag"))
    {
        uint32_t eventId = 0;
        uint32_t requestId = 0;

        if (!JsonFieldToUInt(msg, "event_id", eventId))
        {
            Log("[Pipe] get_event_flag: missing or invalid event_id");
            return;
        }

        if (!JsonFieldToUInt(msg, "request_id", requestId))
        {
            Log("[Pipe] get_event_flag: missing or invalid request_id");
            return;
        }

        bool value = false;
        bool ok = GetEventFlagSafe(eventId, value);
        Logf("[Pipe] get_event_flag: event_flag_id: %u, request_id: %u, ok: %s, value: %u",
            eventId, requestId, ok ? "true" : "false", value ? 1 : 0);

        char response[192];
        sprintf_s(response,
            "{ \"type\":\"event_flag_response\", \"request_id\":%u, \"event_id\":%u, \"ok\":%s, \"is_set\":%s }",
            requestId,
            eventId,
            ok ? "true" : "false",
            value ? "true" : "false");
        g_Pipe.SendJson(std::string(response));
    }

    // --- handle grant_item messages ---   
    if (JsonIsType(msg, "grant_item"))
    {
        uint32_t goodsId = 0;
        uint32_t quantity = 1;
        uint32_t eventId = 0;
        uint32_t deliveryFlagId = 0;
        uint32_t grantRequestId = 0;

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
        JsonFieldToUInt(msg, "delivery_flag_id", deliveryFlagId);
        JsonFieldToUInt(msg, "grant_request_id", grantRequestId);

        Logf("[Pipe] grant_item -> goodsId=%u qty=%u eventId=%u deliveryFlagId=%u grantRequestId=%u",
            goodsId, quantity, eventId, deliveryFlagId, grantRequestId);
        
         if (!IsWorldLoaded())
         {
             Log("[Pipe] grant_item: world is not loaded, queueing for later");
         }

        SekiroGame_QueueGrantItem(eventId, goodsId, quantity, deliveryFlagId, grantRequestId);

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

        SekiroGame_ProcessPendingGrants();
        ItemHooks_ProcessPendingForeignRemovals();

        Sleep(33); // ~30 fps for overlay
    }

    Overlay_Shutdown();

    Logf("[Core] Thread stopped");
    return 0;
}
