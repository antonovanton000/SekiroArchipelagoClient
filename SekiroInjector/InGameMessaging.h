#pragma once
#include <string>

bool InitInGameMessaging();
void InGameMessaging_HandlePipeMessage(const std::string& msg);


void InGameMessaging_ShowHintBox_ById(int headerId, int textId);
void InGameMessaging_ShowSmallHintBox_ById(int textId);
void InGameMessaging_RemoveClientHint();
void InGameMessaging_ShowHint(const wchar_t* text, int headerId);
void InGameMessaging_ShowSmallHint(const wchar_t* text);
void InGameMessaging_ShowSmallHintBoxSimple(int textId);