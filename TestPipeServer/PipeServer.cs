// PipeServer.cs
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SekiroAPClient
{
    public class PipeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts = new();
        private NamedPipeServerStream? _server;
        private Task? _acceptLoopTask;
        private readonly ConcurrentQueue<string> _sendQueue = new();
        private readonly AutoResetEvent _sendEvent = new(false);

        public bool IsConnected => _server is { IsConnected: true };

        /// <summary>
        /// Вызывается при получении JSON-сообщения от DLL.
        /// </summary>
        public event Action<string>? MessageReceived;

        public PipeServer(string pipeName)
        {
            _pipeName = pipeName;
        }

        public void Start()
        {
            // Запускаем фоновую задачу, которая будет:
            // - ждать подключения
            // - после подключения читать/писать в этот пайп
            _acceptLoopTask = Task.Run(AcceptLoopAsync);
        }

        public void Stop()
        {
            _cts.Cancel();
            try
            {
                _acceptLoopTask?.Wait(1000);
            }
            catch { /* ignore */ }

            CloseServer();
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }

        /// <summary>
        /// Поставить JSON-строку в очередь на отправку.
        /// Она уйдёт при первом удобном случае, когда есть подключение.
        /// </summary>
        public void SendJson(string json)
        {
            if (_cts.IsCancellationRequested)
                return;

            _sendQueue.Enqueue(json);
            _sendEvent.Set();
        }

        private async Task AcceptLoopAsync()
        {
            var token = _cts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Создаём серверный пайп (один клиент)
                    _server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1, // max number of server instances
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    Console.WriteLine("[Pipe] Waiting for client...");
                    await _server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    Console.WriteLine("[Pipe] Client connected");

                    // Когда подключились — запускаем две задачи: чтение и запись
                    var readTask = Task.Run(() => ReadLoopAsync(_server, token), token);
                    var writeTask = Task.Run(() => WriteLoopAsync(_server, token), token);

                    // Ждём, пока одна из задач не завершится (чаще всего из-за дисконнекта)
                    await Task.WhenAny(readTask, writeTask).ConfigureAwait(false);

                    Console.WriteLine("[Pipe] Client disconnected");

                    CloseServer();

                    // Небольшая пауза перед следующей попыткой
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Нормальное завершение
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Pipe] Exception in AcceptLoop: " + ex);
                    CloseServer();
                    await Task.Delay(2000, token).ConfigureAwait(false);
                }
            }
        }

        private async Task ReadLoopAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            var lengthBuffer = new byte[4];

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                // Читаем 4 байта длины
                if (!await ReadExactAsync(pipe, lengthBuffer, 0, 4, token).ConfigureAwait(false))
                {
                    // Дисконнект или ошибка
                    break;
                }

                uint length = BitConverter.ToUInt32(lengthBuffer, 0);
                if (length == 0)
                {
                    // Пустое сообщение — пропускаем
                    continue;
                }

                var payload = new byte[length];
                if (!await ReadExactAsync(pipe, payload, 0, (int)length, token).ConfigureAwait(false))
                {
                    break;
                }

                string json = Encoding.UTF8.GetString(payload);

                // Вызываем callback на UI/лог
                try
                {
                    MessageReceived?.Invoke(json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Pipe] MessageReceived handler error: " + ex);
                }
            }
        }

        private async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token)
                                       .ConfigureAwait(false);
                if (read == 0)
                {
                    // Клиент отключился
                    return false;
                }
                totalRead += read;
            }
            return true;
        }

        private async Task WriteLoopAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                // Ждём, пока появятся сообщения в очереди
                if (_sendQueue.IsEmpty)
                {
                    // Либо ждём событие, либо выходим, если отмена
                    WaitHandle.WaitAny(new WaitHandle[] { _sendEvent, token.WaitHandle });
                    if (token.IsCancellationRequested || !pipe.IsConnected)
                        break;
                }

                while (_sendQueue.TryDequeue(out var json))
                {
                    try
                    {
                        byte[] payload = Encoding.UTF8.GetBytes(json);
                        byte[] lengthPrefix = BitConverter.GetBytes((uint)payload.Length);

                        await pipe.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, token)
                                  .ConfigureAwait(false);

                        if (payload.Length > 0)
                        {
                            await pipe.WriteAsync(payload, 0, payload.Length, token)
                                      .ConfigureAwait(false);
                        }

                        await pipe.FlushAsync(token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // Клиент отвалился
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Pipe] Error in WriteLoop: " + ex);
                        return;
                    }
                }
            }
        }

        private void CloseServer()
        {
            try
            {
                _server?.Dispose();
            }
            catch { /* ignore */ }
            _server = null;
        }
    }
}
