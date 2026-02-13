#include "pch.h"
#include "Log.h"
#include <Windows.h>
#include <cstdarg>
#include <cstdio>
#include <ctime>
#include "Core.h"

static void GetTimestamp(char* buf, size_t size)
{
    std::time_t now = std::time(nullptr);
    std::tm tm{};
    localtime_s(&tm, &now);
    strftime(buf, size, "[%Y-%m-%d %H:%M:%S]", &tm);
}

void Log(const char* msg)
{
    HANDLE hFile = CreateFileA(
        "sekiroapclient_log.txt",
        FILE_APPEND_DATA,
        FILE_SHARE_READ,
        nullptr,
        OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr
    );

    if (hFile == INVALID_HANDLE_VALUE)
        return;

    char timeBuf[64];
    GetTimestamp(timeBuf, sizeof(timeBuf));

    DWORD written;
    WriteFile(hFile, timeBuf, (DWORD)strlen(timeBuf), &written, nullptr);
    WriteFile(hFile, " ", 1, &written, nullptr);
    WriteFile(hFile, msg, (DWORD)strlen(msg), &written, nullptr);
    WriteFile(hFile, "\r\n", 2, &written, nullptr);

    CloseHandle(hFile);
}

void Logf(const char* fmt, ...)
{
    if (!g_IsDebug)
        return;

    char buf[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    Log(buf);
}
