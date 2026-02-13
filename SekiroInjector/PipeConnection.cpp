#include "pch.h"
#include "PipeConnection.h"
#include "Log.h"
#include "SekiroGame.h"

PipeConnection::PipeConnection()
    : m_pipe(INVALID_HANDLE_VALUE)
    , m_state(PipeState::Disconnected)
    , m_nextConnectAttemptMs(0)
    , m_connectIntervalMs(2000) // try to connect every 2 seconds
    , m_expectedLength(0)
{
}

PipeConnection::~PipeConnection()
{
    Shutdown();
}

void PipeConnection::Initialize(const std::wstring& pipeName)
{
    m_pipeName = pipeName;
    m_state = PipeState::Disconnected;
    m_nextConnectAttemptMs = 0;
    m_expectedLength = 0;

    // Clear queues just in case
    {
        std::lock_guard<std::mutex> lock(m_sendMutex);
        m_sendQueue.clear();
    }
    {
        std::lock_guard<std::mutex> lock(m_recvMutex);
        m_recvQueue.clear();
    }
}

void PipeConnection::Shutdown()
{
    ClosePipe();

    // Clear queues to avoid dangling messages
    {
        std::lock_guard<std::mutex> lock(m_sendMutex);
        m_sendQueue.clear();
    }
    {
        std::lock_guard<std::mutex> lock(m_recvMutex);
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
    m_expectedLength = 0;
}

bool PipeConnection::SendJson(const std::string& json)
{
    // Even if disconnected, we still buffer messages
    {
        std::lock_guard<std::mutex> lock(m_sendMutex);
        m_sendQueue.push_back(json);
    }
    return true;
}

bool PipeConnection::HasIncoming() const
{
    std::lock_guard<std::mutex> lock(m_recvMutex);
    return !m_recvQueue.empty();
}

bool PipeConnection::PopIncoming(std::string& outMessage)
{
    std::lock_guard<std::mutex> lock(m_recvMutex);
    if (m_recvQueue.empty())
        return false;

    outMessage = std::move(m_recvQueue.front());
    m_recvQueue.pop_front();
    return true;
}

void PipeConnection::Tick()
{
    // 1) If disconnected — attempt reconnection periodically
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

    // 2) If connected — send queued outgoing messages
    ProcessOutgoing();

    // 3) Process incoming messages
    ProcessIncoming();
}

void PipeConnection::TryConnect()
{
    // Attempt to connect to existing named pipe (e.g. "\\\\.\\pipe\\SekiroAP")
    HANDLE hPipe = CreateFileW(
        m_pipeName.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        0,              // no sharing
        nullptr,
        OPEN_EXISTING,
        0,              // synchronous I/O
        nullptr
    );

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        // Connection failed — schedule next attempt
        m_state = PipeState::Disconnected;
        m_pipe = INVALID_HANDLE_VALUE;
        m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
        return;
    }

    // Set pipe to byte-read mode so PeekNamedPipe behaves correctly
    DWORD mode = PIPE_READMODE_BYTE;
    if (!SetNamedPipeHandleState(hPipe, &mode, nullptr, nullptr))
    {
        CloseHandle(hPipe);
        m_state = PipeState::Disconnected;
        m_pipe = INVALID_HANDLE_VALUE;
        m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
        return;
    }

    m_pipe = hPipe;
    m_state = PipeState::Connected;
    m_expectedLength = 0;

    // Immediately send world state after successful connection
    bool isWorldLoaded = IsWorldLoaded();
    char buf[128];
    sprintf_s(buf, sizeof(buf),
        "{ \"type\":\"world\", \"status\": %s }",
        isWorldLoaded ? "true" : "false");

    SendJson(std::string(buf));

    Log("[Pipe] Connected to named pipe");
}

void PipeConnection::ProcessOutgoing()
{
    if (m_pipe == INVALID_HANDLE_VALUE)
        return;

    for (;;)
    {
        std::string msg;
        {
            std::lock_guard<std::mutex> lock(m_sendMutex);
            if (m_sendQueue.empty())
                break;
            msg = m_sendQueue.front();
            m_sendQueue.pop_front();
        }

        uint32_t len = static_cast<uint32_t>(msg.size());

        // Build frame: [4-byte length][payload]
        std::string frame;
        frame.resize(sizeof(uint32_t) + msg.size());

        // Write length in little-endian format
        frame[0] = static_cast<char>(len & 0xFF);
        frame[1] = static_cast<char>((len >> 8) & 0xFF);
        frame[2] = static_cast<char>((len >> 16) & 0xFF);
        frame[3] = static_cast<char>((len >> 24) & 0xFF);

        if (!msg.empty())
            memcpy(&frame[4], msg.data(), msg.size());

        DWORD bytesWritten = 0;
        BOOL ok = WriteFile(
            m_pipe,
            frame.data(),
            static_cast<DWORD>(frame.size()),
            &bytesWritten,
            nullptr
        );

        if (!ok || bytesWritten != frame.size())
        {
            // Write failed — reset connection
            ClosePipe();
            m_state = PipeState::Disconnected;
            m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
            break;
        }
    }
}

void PipeConnection::ProcessIncoming()
{
    if (m_pipe == INVALID_HANDLE_VALUE)
        return;

    for (;;)
    {
        DWORD bytesAvailable = 0;
        BOOL ok = PeekNamedPipe(
            m_pipe,
            nullptr,
            0,
            nullptr,
            &bytesAvailable,
            nullptr
        );

        if (!ok)
        {
            // Pipe error — reset connection
            ClosePipe();
            m_state = PipeState::Disconnected;
            m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
            return;
        }

        if (bytesAvailable == 0)
            break;

        // If length is not yet known — read first 4 bytes
        if (m_expectedLength == 0)
        {
            if (bytesAvailable < sizeof(uint32_t))
                break;

            uint32_t len = 0;
            DWORD bytesRead = 0;

            ok = ReadFile(
                m_pipe,
                &len,
                sizeof(uint32_t),
                &bytesRead,
                nullptr
            );

            if (!ok || bytesRead != sizeof(uint32_t))
            {
                ClosePipe();
                m_state = PipeState::Disconnected;
                m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
                return;
            }

            m_expectedLength = len;
            bytesAvailable -= sizeof(uint32_t);
        }

        // Wait until full payload is available
        if (bytesAvailable < m_expectedLength)
            break;

        if (m_expectedLength > 0)
        {
            std::string payload;
            payload.resize(m_expectedLength);

            DWORD bytesRead = 0;
            ok = ReadFile(
                m_pipe,
                payload.data(),
                m_expectedLength,
                &bytesRead,
                nullptr
            );

            if (!ok || bytesRead != m_expectedLength)
            {
                ClosePipe();
                m_state = PipeState::Disconnected;
                m_nextConnectAttemptMs = GetTickCount64() + m_connectIntervalMs;
                return;
            }

            {
                std::lock_guard<std::mutex> lock(m_recvMutex);
                m_recvQueue.push_back(std::move(payload));
            }

            m_expectedLength = 0;

            // Continue loop in case more messages are already buffered
            continue;
        }
    }
}
