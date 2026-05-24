#include "pch.h"
#include "PipeConnection.h"
#include "Log.h"
#include "SekiroGame.h"

#include <cstdio>

#pragma comment(lib, "Ws2_32.lib")

static constexpr uint32_t MAX_FRAME_SIZE = 64 * 1024;

class CriticalSectionLock
{
public:
    explicit CriticalSectionLock(CRITICAL_SECTION& section)
        : m_section(section)
    {
        EnterCriticalSection(&m_section);
    }

    ~CriticalSectionLock()
    {
        LeaveCriticalSection(&m_section);
    }

private:
    CRITICAL_SECTION& m_section;
};

static bool EqualsIgnoreCaseAscii(const char* left, const char* right)
{
    if (!left || !right)
        return false;

    while (*left && *right)
    {
        char a = *left++;
        char b = *right++;
        if (a >= 'A' && a <= 'Z') a = static_cast<char>(a - 'A' + 'a');
        if (b >= 'A' && b <= 'Z') b = static_cast<char>(b - 'A' + 'a');
        if (a != b)
            return false;
    }

    return *left == '\0' && *right == '\0';
}

static void TrimAsciiInPlace(char* value)
{
    if (!value)
        return;

    char* start = value;
    while (*start == ' ' || *start == '\t' || *start == '\r' || *start == '\n')
        ++start;

    if (start != value)
        memmove(value, start, strlen(start) + 1);

    size_t len = strlen(value);
    while (len > 0)
    {
        char ch = value[len - 1];
        if (ch != ' ' && ch != '\t' && ch != '\r' && ch != '\n')
            break;
        value[--len] = '\0';
    }
}

static bool TryReadTransportConfig(char* buffer, DWORD bufferSize)
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&TryReadTransportConfig),
        &module))
    {
        return false;
    }

    wchar_t path[MAX_PATH] = {};
    if (!GetModuleFileNameW(module, path, MAX_PATH))
        return false;

    wchar_t* slash = wcsrchr(path, L'\\');
    wchar_t* slash2 = wcsrchr(path, L'/');
    if (!slash || (slash2 && slash2 > slash))
        slash = slash2;
    if (!slash)
        return false;

    *(slash + 1) = L'\0';
    wcscat_s(path, L"connection_transport.txt");

    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return false;

    DWORD read = 0;
    BOOL ok = ReadFile(file, buffer, bufferSize - 1, &read, nullptr);
    CloseHandle(file);
    if (!ok || read == 0)
        return false;

    buffer[read] = '\0';
    TrimAsciiInPlace(buffer);
    return true;
}

PipeConnection::PipeConnection()
    : m_pipe(INVALID_HANDLE_VALUE)
    , m_socket(INVALID_SOCKET)
    , m_state(PipeState::Disconnected)
    , m_transport(ConnectionTransport::NamedPipe)
    , m_nextConnectAttemptMs(0)
    , m_connectIntervalMs(2000)
    , m_expectedLength(0)
    , m_tcpHost("127.0.0.1")
    , m_tcpPort(38571)
    , m_wsaStarted(false)
{
    InitializeCriticalSection(&m_sendLock);
    InitializeCriticalSection(&m_recvLock);
    m_pipeName[0] = L'\0';
}

PipeConnection::~PipeConnection()
{
    Shutdown();
    DeleteCriticalSection(&m_sendLock);
    DeleteCriticalSection(&m_recvLock);
}

void PipeConnection::Initialize(const wchar_t* pipeName)
{
    if (pipeName)
        wcsncpy_s(m_pipeName, pipeName, _TRUNCATE);
    else
        m_pipeName[0] = L'\0';

    SelectTransport();

    m_state = PipeState::Disconnected;
    m_nextConnectAttemptMs = 0;
    m_expectedLength = 0;
}

void PipeConnection::Shutdown()
{
    ClosePipe();

    if (m_wsaStarted)
    {
        WSACleanup();
        m_wsaStarted = false;
    }

    {
        CriticalSectionLock lock(m_sendLock);
        m_sendQueue.clear();
    }
    {
        CriticalSectionLock lock(m_recvLock);
        m_recvQueue.clear();
    }

    m_state = PipeState::Disconnected;
}

void PipeConnection::ClosePipe()
{
    if (m_pipe != INVALID_HANDLE_VALUE)
    {
        CloseHandle(m_pipe);
        m_pipe = INVALID_HANDLE_VALUE;
    }

    if (m_socket != INVALID_SOCKET)
    {
        closesocket(m_socket);
        m_socket = INVALID_SOCKET;
    }

    m_expectedLength = 0;
}

bool PipeConnection::SendJson(const std::string& json)
{
    {
        CriticalSectionLock lock(m_sendLock);
        m_sendQueue.push_back(json);
    }
    return true;
}

bool PipeConnection::HasIncoming() const
{
    CriticalSectionLock lock(m_recvLock);
    return !m_recvQueue.empty();
}

bool PipeConnection::PopIncoming(std::string& outMessage)
{
    CriticalSectionLock lock(m_recvLock);
    if (m_recvQueue.empty())
        return false;

    outMessage = std::move(m_recvQueue.front());
    m_recvQueue.pop_front();
    return true;
}

void PipeConnection::Tick()
{
    if (m_state == PipeState::Disconnected)
    {
        ULONGLONG now = GetTickCount64();
        if (now >= m_nextConnectAttemptMs)
        {
            m_state = PipeState::Connecting;
            TryConnect();
        }
        return;
    }

    if (m_state != PipeState::Connected)
        return;

    ProcessOutgoing();
    ProcessIncoming();
}

void PipeConnection::TryConnect()
{
    bool connected = (m_transport == ConnectionTransport::Tcp)
        ? TryConnectTcp()
        : TryConnectNamedPipe();

    if (!connected)
    {
        m_state = PipeState::Disconnected;
        m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
        return;
    }

    m_state = PipeState::Connected;
    m_expectedLength = 0;

    Log(m_transport == ConnectionTransport::Tcp
        ? "[Pipe] Connected to TCP transport"
        : "[Pipe] Connected to named pipe");

    const bool isWorldLoaded = IsWorldLoaded();
    char worldState[80];
    sprintf_s(worldState,
        "{ \"type\":\"world\", \"status\": %s }",
        isWorldLoaded ? "true" : "false");
    Logf("[Pipe] Sending initial world state: %s", isWorldLoaded ? "true" : "false");
    SendJson(std::string(worldState));
}

bool PipeConnection::TryConnectNamedPipe()
{
    HANDLE hPipe = CreateFileW(
        m_pipeName,
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        m_pipe = INVALID_HANDLE_VALUE;
        return false;
    }

    DWORD mode = PIPE_READMODE_BYTE;
    if (!SetNamedPipeHandleState(hPipe, &mode, nullptr, nullptr))
    {
        CloseHandle(hPipe);
        m_pipe = INVALID_HANDLE_VALUE;
        return false;
    }

    m_pipe = hPipe;
    return true;
}

bool PipeConnection::TryConnectTcp()
{
    if (!m_wsaStarted)
    {
        WSADATA data{};
        if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
        {
            Logf("[Pipe] WSAStartup failed err=%d", WSAGetLastError());
            return false;
        }
        m_wsaStarted = true;
    }

    SOCKET sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock == INVALID_SOCKET)
    {
        Logf("[Pipe] socket failed err=%d", WSAGetLastError());
        return false;
    }

    u_long nonBlocking = 1;
    ioctlsocket(sock, FIONBIO, &nonBlocking);

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(m_tcpPort);
    inet_pton(AF_INET, m_tcpHost.c_str(), &addr.sin_addr);

    int result = connect(sock, reinterpret_cast<sockaddr*>(&addr), sizeof(addr));
    if (result == SOCKET_ERROR)
    {
        int err = WSAGetLastError();
        if (err != WSAEWOULDBLOCK && err != WSAEINPROGRESS && err != WSAEINVAL)
        {
            closesocket(sock);
            return false;
        }

        fd_set writeSet;
        FD_ZERO(&writeSet);
        FD_SET(sock, &writeSet);

        timeval timeout{};
        timeout.tv_usec = 100000;

        result = select(0, nullptr, &writeSet, nullptr, &timeout);
        if (result <= 0)
        {
            closesocket(sock);
            return false;
        }
    }

    BOOL noDelay = TRUE;
    setsockopt(sock, IPPROTO_TCP, TCP_NODELAY, reinterpret_cast<const char*>(&noDelay), sizeof(noDelay));

    m_socket = sock;
    return true;
}

void PipeConnection::ProcessOutgoing()
{
    if (m_transport == ConnectionTransport::NamedPipe && m_pipe == INVALID_HANDLE_VALUE)
        return;
    if (m_transport == ConnectionTransport::Tcp && m_socket == INVALID_SOCKET)
        return;

    for (;;)
    {
        std::string msg;
        {
            CriticalSectionLock lock(m_sendLock);
            if (m_sendQueue.empty())
                break;
            msg = m_sendQueue.front();
            m_sendQueue.pop_front();
        }

        uint32_t len = static_cast<uint32_t>(msg.size());
        std::string frame;
        frame.resize(sizeof(uint32_t) + msg.size());
        frame[0] = static_cast<char>(len & 0xFF);
        frame[1] = static_cast<char>((len >> 8) & 0xFF);
        frame[2] = static_cast<char>((len >> 16) & 0xFF);
        frame[3] = static_cast<char>((len >> 24) & 0xFF);

        if (!msg.empty())
            memcpy(&frame[4], msg.data(), msg.size());

        if (!WriteBytes(frame.data(), static_cast<DWORD>(frame.size())))
        {
            Log("[Pipe] Write failed, disconnecting");
            ClosePipe();
            m_state = PipeState::Disconnected;
            m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
            break;
        }
    }
}

void PipeConnection::ProcessIncoming()
{
    if (m_transport == ConnectionTransport::NamedPipe && m_pipe == INVALID_HANDLE_VALUE)
        return;
    if (m_transport == ConnectionTransport::Tcp && m_socket == INVALID_SOCKET)
        return;

    for (;;)
    {
        DWORD bytesAvailable = 0;
        if (!PeekAvailable(bytesAvailable))
        {
            ClosePipe();
            m_state = PipeState::Disconnected;
            m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
            return;
        }

        if (bytesAvailable == 0)
            break;

        if (m_expectedLength == 0)
        {
            if (bytesAvailable < sizeof(uint32_t))
                break;

            uint32_t len = 0;
            DWORD bytesRead = 0;
            if (!ReadBytes(&len, sizeof(uint32_t), bytesRead) || bytesRead != sizeof(uint32_t))
            {
                ClosePipe();
                m_state = PipeState::Disconnected;
                m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
                return;
            }

            m_expectedLength = len;
            if (m_expectedLength > MAX_FRAME_SIZE)
            {
                Logf("[Pipe] Incoming frame too large: %u, disconnecting", m_expectedLength);
                ClosePipe();
                m_state = PipeState::Disconnected;
                m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
                return;
            }

            bytesAvailable -= sizeof(uint32_t);
        }

        if (bytesAvailable < m_expectedLength)
            break;

        if (m_expectedLength > 0)
        {
            std::string payload;
            payload.resize(m_expectedLength);

            DWORD bytesRead = 0;
            if (!ReadBytes(payload.data(), m_expectedLength, bytesRead) || bytesRead != m_expectedLength)
            {
                ClosePipe();
                m_state = PipeState::Disconnected;
                m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
                return;
            }

            {
                CriticalSectionLock lock(m_recvLock);
                m_recvQueue.push_back(std::move(payload));
            }

            m_expectedLength = 0;
            continue;
        }
    }
}

void PipeConnection::SelectTransport()
{
    m_transport = ConnectionTransport::NamedPipe;
    m_tcpHost = "127.0.0.1";
    m_tcpPort = 38571;

    char envTransport[32] = {};
    if (GetEnvironmentVariableA("SEKIRO_AP_TRANSPORT", envTransport, sizeof(envTransport)) > 0)
    {
        TrimAsciiInPlace(envTransport);
        if (EqualsIgnoreCaseAscii(envTransport, "tcp"))
            m_transport = ConnectionTransport::Tcp;
    }

    char envPort[16] = {};
    if (GetEnvironmentVariableA("SEKIRO_AP_TCP_PORT", envPort, sizeof(envPort)) > 0)
    {
        int parsed = atoi(envPort);
        if (parsed > 0 && parsed <= 65535)
            m_tcpPort = static_cast<uint16_t>(parsed);
    }

    char config[32] = {};
    if (TryReadTransportConfig(config, sizeof(config)))
    {
        if (EqualsIgnoreCaseAscii(config, "tcp"))
            m_transport = ConnectionTransport::Tcp;
        else if (EqualsIgnoreCaseAscii(config, "namedpipe") || EqualsIgnoreCaseAscii(config, "pipe"))
            m_transport = ConnectionTransport::NamedPipe;
    }

    Log(m_transport == ConnectionTransport::Tcp
        ? "[Pipe] Transport selected: TCP"
        : "[Pipe] Transport selected: NamedPipe");
}

bool PipeConnection::WriteBytes(const char* data, DWORD size)
{
    if (m_transport == ConnectionTransport::NamedPipe)
    {
        DWORD bytesWritten = 0;
        BOOL ok = WriteFile(m_pipe, data, size, &bytesWritten, nullptr);
        return ok && bytesWritten == size;
    }

    DWORD total = 0;
    while (total < size)
    {
        int sent = send(m_socket, data + total, static_cast<int>(size - total), 0);
        if (sent == SOCKET_ERROR)
        {
            int err = WSAGetLastError();
            if (err == WSAEWOULDBLOCK)
            {
                Sleep(1);
                continue;
            }
            return false;
        }
        if (sent == 0)
            return false;
        total += static_cast<DWORD>(sent);
    }

    return true;
}

bool PipeConnection::ReadBytes(void* data, DWORD size, DWORD& bytesRead)
{
    bytesRead = 0;
    if (m_transport == ConnectionTransport::NamedPipe)
    {
        BOOL ok = ReadFile(m_pipe, data, size, &bytesRead, nullptr);
        return ok;
    }

    int read = recv(m_socket, reinterpret_cast<char*>(data), static_cast<int>(size), 0);
    if (read == SOCKET_ERROR)
    {
        int err = WSAGetLastError();
        return err == WSAEWOULDBLOCK;
    }
    if (read == 0)
        return false;

    bytesRead = static_cast<DWORD>(read);
    return true;
}

bool PipeConnection::PeekAvailable(DWORD& bytesAvailable)
{
    bytesAvailable = 0;
    if (m_transport == ConnectionTransport::NamedPipe)
    {
        BOOL ok = PeekNamedPipe(m_pipe, nullptr, 0, nullptr, &bytesAvailable, nullptr);
        return ok;
    }

    u_long available = 0;
    if (ioctlsocket(m_socket, FIONREAD, &available) == SOCKET_ERROR)
        return false;

    bytesAvailable = static_cast<DWORD>(available);
    return true;
}
