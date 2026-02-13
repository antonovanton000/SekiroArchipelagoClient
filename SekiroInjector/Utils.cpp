#include "pch.h"
#include "Utils.h"
#include "Core.h"
#include "Log.h"
#include <cctype>
#include <string>
#include <vector>
#include <fstream>
#include <cstdlib>


uint32_t DecodeGoodsId(uint32_t encoded)
{
    // EncodeGoodsId: (4u << 28) | (goodsId & 0x0FFFFFFF)
    return (encoded & 0x0FFFFFFF);
}

bool SafeReadPtr(uintptr_t addr, uintptr_t& out)
{
    if (!addr) return false;

    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;

    const DWORD ok =
        PAGE_READONLY | PAGE_READWRITE |
        PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE;

    if (!(mbi.Protect & ok)) return false;

    __try {
        out = *(uintptr_t*)addr;
        return out != 0;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        out = 0;
        return false;
    }
}

bool SafeReadInt(uintptr_t addr, int& out)
{
    if (!addr) return false;

    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;

    const DWORD ok =
        PAGE_READONLY | PAGE_READWRITE |
        PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE;

    if (!(mbi.Protect & ok)) return false;

    __try {
        out = *(int*)addr;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        out = 0;
        return false;
    }
}

bool SafeReadFloat(uintptr_t addr, float& out)
{
    if (!addr)
        return false;

    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi)))
        return false;

    if (mbi.State != MEM_COMMIT)
        return false;

    const DWORD ok =
        PAGE_READONLY | PAGE_READWRITE |
        PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE;

    if (!(mbi.Protect & ok))
        return false;

    __try
    {
        out = *(float*)addr;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        out = 0.0f;
        return false;
    }
}

bool SafeWriteInt(uintptr_t addr, int value)
{
    if (!addr)
        return false;

    DWORD oldProtect = 0;

    __try
    {
        if (!VirtualProtect(reinterpret_cast<void*>(addr), sizeof(int),
            PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            return false;
        }

        *reinterpret_cast<int*>(addr) = value;

        VirtualProtect(reinterpret_cast<void*>(addr), sizeof(int), oldProtect, &oldProtect);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

inline uint32_t MakeRawItemId(uint32_t itemId, ItemCategory cat)
{
    return static_cast<uint32_t>(cat) | itemId;
}

bool JsonFieldToUInt(const std::string& json, const char* name, uint32_t& outVal)
{
    // Ищем имя поля
    std::string needle = "\"";
    needle += name;
    needle += "\"";

    size_t pos = json.find(needle);
    if (pos == std::string::npos)
        return false;

    // Ищем двоеточие после имени
    pos = json.find(':', pos);
    if (pos == std::string::npos)
        return false;

    ++pos; // после ':'

    // Пропускаем пробелы
    while (pos < json.size() && std::isspace(static_cast<unsigned char>(json[pos])))
        ++pos;

    // Собираем цифры
    bool hasDigit = false;
    unsigned long value = 0;
    while (pos < json.size() && std::isdigit(static_cast<unsigned char>(json[pos])))
    {
        hasDigit = true;
        value = value * 10 + (json[pos] - '0');
        ++pos;
    }

    if (!hasDigit)
        return false;

    outVal = static_cast<uint32_t>(value);
    return true;
}

bool JsonFieldToBool(const std::string& json, const char* name, bool& outVal)
{
    // Ищем имя поля
    std::string needle = "\"";
    needle += name;
    needle += "\"";

    size_t pos = json.find(needle);
    if (pos == std::string::npos)
        return false;

    // Ищем двоеточие после имени
    pos = json.find(':', pos);
    if (pos == std::string::npos)
        return false;

    ++pos; // после ':'

    // Пропускаем пробелы
    while (pos < json.size() && std::isspace(static_cast<unsigned char>(json[pos])))
        ++pos;

    auto matchWord = [&](const char* word, size_t len) -> bool
        {
            if (pos + len > json.size())
                return false;

            for (size_t i = 0; i < len; ++i)
            {
                if (std::tolower(static_cast<unsigned char>(json[pos + i])) != word[i])
                    return false;
            }
            return true;
        };

    if (matchWord("true", 4))
    {
        outVal = true;
        return true;
    }

    if (matchWord("false", 5))
    {
        outVal = false;
        return true;
    }

    return false;
}

bool JsonFieldToInt(const std::string& json, const char* name, int& outVal)
{
    // Ищем имя поля
    std::string needle = "\"";
    needle += name;
    needle += "\"";

    size_t pos = json.find(needle);
    if (pos == std::string::npos)
        return false;

    // Ищем двоеточие после имени
    pos = json.find(':', pos);
    if (pos == std::string::npos)
        return false;

    ++pos; // после ':'

    // Пропускаем пробелы
    while (pos < json.size() &&
        std::isspace(static_cast<unsigned char>(json[pos])))
    {
        ++pos;
    }

    // Обрабатываем знак
    bool negative = false;
    if (pos < json.size() && json[pos] == '-')
    {
        negative = true;
        ++pos;
    }

    // Собираем цифры
    bool hasDigit = false;
    int value = 0;

    while (pos < json.size() &&
        std::isdigit(static_cast<unsigned char>(json[pos])))
    {
        hasDigit = true;
        value = value * 10 + (json[pos] - '0');
        ++pos;
    }

    if (!hasDigit)
        return false;

    outVal = negative ? -value : value;
    return true;
}

// Проверяем, что "type":"grant_item"
bool JsonIsType(const std::string& json, const char* typeName)
{
    // Для простоты рассчитываем на подстроку без пробелов внутри:
    // "type":"grant_item"
    std::string needle = "\"type\":\"";
    needle += typeName;
    needle += "\"";

    return json.find(needle) != std::string::npos;
}
bool JsonFieldToString(const std::string& json, const char* field, std::string& out)
{
    out.clear();
    if (!field || !*field)
        return false;

    std::string key = "\"";
    key += field;
    key += "\"";

    size_t keyPos = json.find(key);
    if (keyPos == std::string::npos)
        return false;

    // Ищем двоеточие после ключа
    size_t colonPos = json.find(':', keyPos + key.size());
    if (colonPos == std::string::npos)
        return false;

    // Пропускаем пробелы
    size_t valuePos = colonPos + 1;
    while (valuePos < json.size() && (json[valuePos] == ' ' || json[valuePos] == '\t'))
        ++valuePos;

    // Значение должно начинаться с кавычки
    if (valuePos >= json.size() || json[valuePos] != '"')
        return false;

    ++valuePos; // после первой кавычки

    // Ищем закрывающую кавычку
    size_t endPos = valuePos;
    while (endPos < json.size())
    {
        if (json[endPos] == '"')
            break;
        ++endPos;
    }

    if (endPos >= json.size())
        return false;

    out.assign(json.begin() + valuePos, json.begin() + endPos);
    return true;
}


constexpr size_t HINT_LINE_WIDTH = 40;
constexpr size_t HINT_ELLIPSIS_LEN = 3;

// Обрезка строки под "…"
std::wstring TruncateWithDots(const std::wstring& s, size_t maxLen)
{
    if (s.size() <= maxLen) return s;
    if (maxLen <= HINT_ELLIPSIS_LEN) return std::wstring(L"...", L"..." + HINT_ELLIPSIS_LEN);
    std::wstring res = s.substr(0, maxLen - HINT_ELLIPSIS_LEN);
    res += L"...";
    return res;
}

// 1. Заменяем ЛИТЕРАЛЬНЫЕ "\n" и "\r\n" на настоящий L'\n'
std::wstring UnescapeBackslashNewlines(const std::wstring& input)
{
    std::wstring out;
    out.reserve(input.size());

    for (size_t i = 0; i < input.size(); ++i)
    {
        if (input[i] == L'\\' && i + 1 < input.size())
        {
            wchar_t next = input[i + 1];

            if (next == L'n')
            {
                out.push_back(L'\n'); // "\n" -> перевод строки
                ++i;
                continue;
            }
            if (next == L'r')
            {
                // "\r" или "\r\n" -> просто перевод строки
                out.push_back(L'\n');
                ++i;
                // если дальше ещё 'n' — съедаем его
                if (i + 1 < input.size() && input[i + 1] == L'n')
                    ++i;
                continue;
            }
        }

        out.push_back(input[i]);
    }

    return out;
}

// 2. Нормализуем реальные \r / \r\n в просто \n (на всякий случай)
std::wstring NormalizeRealNewlines(const std::wstring& input)
{
    std::wstring out;
    out.reserve(input.size());

    for (size_t i = 0; i < input.size(); ++i)
    {
        if (input[i] == L'\r')
        {
            if (i + 1 < input.size() && input[i + 1] == L'\n')
                ++i; // пропускаем \n

            out.push_back(L'\n');
        }
        else
        {
            out.push_back(input[i]);
        }
    }

    return out;
}

// Финальный хелпер: строка "как из C#" -> с нормальными \n
std::wstring FixHintTextFromCSharp(const std::wstring& raw)
{
    std::wstring step1 = UnescapeBackslashNewlines(raw);
    std::wstring step2 = NormalizeRealNewlines(step1);
    return step2;
}


bool LoadIdListFromFile(
    const char* path,
    uint32_t* outArray,
    int maxCount,
    int& outCount)
{
    outCount = 0;

    std::ifstream file(path);
    if (!file.is_open())
    {
        Logf("[ForeignIds] Failed to open %s", path);
        return false;
    }

    std::string content;
    std::getline(file, content);
    file.close();

    // remove UTF-8 BOM if present
    if (content.size() >= 3 &&
        (unsigned char)content[0] == 0xEF &&
        (unsigned char)content[1] == 0xBB &&
        (unsigned char)content[2] == 0xBF)
    {
        content.erase(0, 3);
    }

    size_t pos = 0;
    while (pos < content.size() && outCount < maxCount)
    {
        size_t next = content.find(';', pos);
        if (next == std::string::npos)
            next = content.size();

        std::string token = content.substr(pos, next - pos);

        if (!token.empty())
        {
            char* endPtr = nullptr;
            uint32_t id = static_cast<uint32_t>(strtoul(token.c_str(), &endPtr, 10));

            if (endPtr != token.c_str() && id != 0)
            {
                outArray[outCount++] = id;
            }
        }

        pos = next + 1;
    }

    return true;
}


bool IsForeignPickupLot(uint32_t lotId)
{
    if (!g_ForeignPickupLotsInitialized)
        return false;

    for (int i = 0; i < g_ForeignPickupLotsCount; ++i)
    {
        if (g_ForeignPickupLots[i] == lotId)
            return true;
    }
    return false;
}

bool IsForeignShopLot(uint32_t lineupId)
{
    if (!g_ForeignShopLotsInitialized)
        return false;

    for (int i = 0; i < g_ForeignShopLotsCount; ++i)
    {
        if (g_ForeignShopLots[i] == lineupId)
            return true;
    }
    return false;
}