#pragma once

#include <cstdint>
#include <string> 


bool SafeReadPtr(uintptr_t addr, uintptr_t& out);
bool SafeReadByte(uintptr_t addr, uint8_t& out);
bool SafeReadInt(uintptr_t addr, int& out);
bool SafeReadFloat(uintptr_t addr, float& out);
bool SafeWriteByte(uintptr_t addr, uint8_t value);
bool SafeWriteInt(uintptr_t addr, int value);

enum class ItemCategory : uint32_t
{
    Weapon = 0x00000000,
    Protector = 0x10000000,
    Accessory = 0x20000000,
    Goods = 0x40000000,
};

uint32_t MakeRawItemId(uint32_t itemId, ItemCategory cat);
uint32_t DecodeGoodsId(uint32_t encoded);

// --- JSON utils ---
bool JsonIsType(const std::string& json, const char* typeName);
bool JsonFieldToUInt(const std::string& json, const char* name, uint32_t& outVal);
bool JsonFieldToInt(const std::string& json, const char* name, int& outVal);
bool JsonFieldToString(const std::string& json, const char* field, std::string& out);
bool JsonFieldToBool(const std::string& json, const char* name, bool& outVal);
std::wstring FixHintTextFromCSharp(const std::wstring& raw);
