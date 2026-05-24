#pragma once

#include <windows.h>
#include <string>
#include <deque>

enum class PipeState
{
    Disconnected,
    Connecting,
    Connected
};

enum class ConnectionTransport
{
    NamedPipe,
    Tcp
};

// Класс-клиент для named pipe \\.\pipe\SekiroAP
// usage:
//   g_Pipe.Initialize(L"\\\\.\\pipe\\SekiroAP");
//   в CoreThread: g_Pipe.Tick();
//   из хуков: g_Pipe.SendJson(json);
class PipeConnection
{
public:
    PipeConnection();
    ~PipeConnection();

    // Задать имя пайпа и подготовить состояние.
    // Реальное подключение делается лениво в Tick().
    void Initialize(const wchar_t* pipeName);

    // Закрыть пайп (можно вызывать при завершении DLL).
    void Shutdown();

    // Вызывать регулярно из CoreThread (например, раз в 30–100 мс).
    // Здесь:
    //  - пытаемся подключиться, если ещё нет
    //  - отправляем накопленные сообщения
    //  - читаем входящие сообщения
    void Tick();

    // Поставить JSON-строку в очередь на отправку.
    // Возвращает false, если DLL уже в состоянии Shutdown.
    bool SendJson(const std::string& json);

    // Проверить, есть ли входящие сообщения.
    bool HasIncoming() const;

    // Забрать одно входящее сообщение (FIFO).
    // Возвращает true, если что-то было.
    bool PopIncoming(std::string& outMessage);

    PipeState GetState() const { return m_state; }
    
private:
    void TryConnect();
    void ClosePipe();
    void ProcessOutgoing();
    void ProcessIncoming();
    void SelectTransport();
    bool TryConnectNamedPipe();
    bool TryConnectTcp();
    bool WriteBytes(const char* data, DWORD size);
    bool ReadBytes(void* data, DWORD size, DWORD& bytesRead);
    bool PeekAvailable(DWORD& bytesAvailable);

private:
    HANDLE      m_pipe;
    SOCKET      m_socket;
    PipeState   m_state;
    ConnectionTransport m_transport;
    wchar_t     m_pipeName[MAX_PATH];
    std::string m_tcpHost;
    uint16_t    m_tcpPort;
    bool        m_wsaStarted;

    // Для reconnect-логики
    ULONGLONG   m_nextConnectAttemptMs;
    DWORD       m_connectIntervalMs; // интервал между попытками

    // Очередь исходящих сообщений
    mutable CRITICAL_SECTION  m_sendLock;
    std::deque<std::string>   m_sendQueue;

    // Очередь входящих сообщений
    mutable CRITICAL_SECTION  m_recvLock;
    std::deque<std::string>   m_recvQueue;

    // Стейт для парсинга входящего стрима (length + payload)
    uint32_t     m_expectedLength;  // 0 = ждём длину
};
