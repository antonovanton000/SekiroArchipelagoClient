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

static bool IsReadableMemoryProtection(DWORD protect)
{
    protect &= ~(PAGE_GUARD | PAGE_NOCACHE | PAGE_WRITECOMBINE);

    switch (protect)
    {
    case PAGE_READONLY:
    case PAGE_READWRITE:
    case PAGE_WRITECOPY:
    case PAGE_EXECUTE_READ:
    case PAGE_EXECUTE_READWRITE:
    case PAGE_EXECUTE_WRITECOPY:
        return true;
    default:
        return false;
    }
}

bool SafeReadPtr(uintptr_t addr, uintptr_t& out)
{
    if (!addr) return false;

    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;

    if (!IsReadableMemoryProtection(mbi.Protect)) return false;

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

    if (!IsReadableMemoryProtection(mbi.Protect)) return false;

    __try {
        out = *(int*)addr;
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        out = 0;
        return false;
    }
}

bool SafeReadByte(uintptr_t addr, uint8_t& out)
{
    if (!addr) return false;

    MEMORY_BASIC_INFORMATION mbi{};
    if (!VirtualQuery((LPCVOID)addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;

    if (!IsReadableMemoryProtection(mbi.Protect)) return false;

    __try {
        out = *(uint8_t*)addr;
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

    if (!IsReadableMemoryProtection(mbi.Protect))
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

bool SafeWriteByte(uintptr_t addr, uint8_t value)
{
    if (!addr)
        return false;

    DWORD oldProtect = 0;

    __try
    {
        if (!VirtualProtect(reinterpret_cast<void*>(addr), sizeof(uint8_t),
            PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            return false;
        }

        *reinterpret_cast<uint8_t*>(addr) = value;

        VirtualProtect(reinterpret_cast<void*>(addr), sizeof(uint8_t), oldProtect, &oldProtect);
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
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

std::wstring TruncateWithDots(const std::wstring& s, size_t maxLen)
{
    if (s.size() <= maxLen) return s;
    if (maxLen <= HINT_ELLIPSIS_LEN) return std::wstring(L"...", L"..." + HINT_ELLIPSIS_LEN);
    std::wstring res = s.substr(0, maxLen - HINT_ELLIPSIS_LEN);
    res += L"...";
    return res;
}


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
                out.push_back(L'\n'); 
                ++i;
                continue;
            }
            if (next == L'r')
            {                
                out.push_back(L'\n');
                ++i;                
                if (i + 1 < input.size() && input[i + 1] == L'n')
                    ++i;
                continue;
            }
        }

        out.push_back(input[i]);
    }

    return out;
}

static inline int HexVal(wchar_t c)
{
    if (c >= L'0' && c <= L'9') return c - L'0';
    if (c >= L'a' && c <= L'f') return 10 + (c - L'a');
    if (c >= L'A' && c <= L'F') return 10 + (c - L'A');
    return -1;
}

inline std::wstring UnescapeUnicodeUxxxx(const std::wstring& input)
{
    std::wstring out;
    out.reserve(input.size());

    for (size_t i = 0; i < input.size(); ++i)
    {
        if (input[i] == L'\\' && i + 5 < input.size() && input[i + 1] == L'u')
        {
            int h1 = HexVal(input[i + 2]);
            int h2 = HexVal(input[i + 3]);
            int h3 = HexVal(input[i + 4]);
            int h4 = HexVal(input[i + 5]);

            if (h1 >= 0 && h2 >= 0 && h3 >= 0 && h4 >= 0)
            {
                wchar_t ch = (wchar_t)((h1 << 12) | (h2 << 8) | (h3 << 4) | h4);
                out.push_back(ch);
                i += 5;
                continue;
            }
        }

        out.push_back(input[i]);
    }

    return out;
}

std::wstring NormalizeRealNewlines(const std::wstring& input)
{
    std::wstring out;
    out.reserve(input.size());

    for (size_t i = 0; i < input.size(); ++i)
    {
        if (input[i] == L'\r')
        {
            if (i + 1 < input.size() && input[i + 1] == L'\n')
                ++i; 

            out.push_back(L'\n');
        }
        else
        {
            out.push_back(input[i]);
        }
    }

    return out;
}

std::wstring FixHintTextFromCSharp(const std::wstring& raw)
{
    std::wstring s = UnescapeUnicodeUxxxx(raw);
    std::wstring step1 = UnescapeBackslashNewlines(s);
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
