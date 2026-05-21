// PipeServer.cs
using Newtonsoft.Json.Linq;
using SekiroAPClient.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Converters;

namespace SekiroAPClient;

public class PipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _server;
    private Task? _acceptLoopTask;
    private readonly ConcurrentQueue<string> _sendQueue = new();
    private readonly AutoResetEvent _sendEvent = new(false);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool?>> _eventFlagRequests = new();
    private int _eventFlagRequestId;
    public bool IsStarted { get; private set; }

    public bool IsConnected => _server is { IsConnected: true };
    public bool ShowDebugLog { get; set; } = false;

    public bool IsWorldLoaded
    {
        get => _isWorldLoaded;
        set
        {
            if (_isWorldLoaded == value)
                return;

            _isWorldLoaded = value;

            // Мир только что загрузился → планируем сброс через 8 секунд
            if (_isWorldLoaded)
            {
                ScheduleSpawnQueueFlush();
            }
        }
    }

    private readonly object _spawnQueueLock = new();
    private readonly Queue<SpawnItemRequest> _spawnQueue = new();

    private bool _isWorldLoaded;
    private bool _flushScheduled;

    /// <summary>
    /// Вызывается при получении JSON-сообщения от DLL.
    /// </summary>
    public event Action<string>? MessageReceived;
    public event Action<ItemRecievedArgs>? ItemReceived;
    public event Action<string>? DebugLodReceived;
    public event Action<string>? ConnectionChanged;
    public event Action<bool>? WorldStateChanged;
    public event Action<bool>? PlayerDeath;
    public event Action<string>? EndingDetected;


    static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), // или UnsafeRelaxedJsonEscaping ниже
    };

    static readonly JsonSerializerOptions JsonOptsRelaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PipeServer(string pipeName)
    {
        _pipeName = pipeName;
        IsStarted = false;
    }

    public void Start()
    {
        // Запускаем фоновую задачу, которая будет:
        // - ждать подключения
        // - после подключения читать/писать в этот пайп
        IsStarted = true;
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

        LogDebug(json);
        _sendQueue.Enqueue(json);
        _sendEvent.Set();
    }

    private bool _showConnectionHintRunning;

    public async Task ShowConnectedToServerAsync(string message)
    {
        if (_showConnectionHintRunning)
            return;

        _showConnectionHintRunning = true;

        try
        {
            while (!IsWorldLoaded)
            {
                await Task.Delay(500);
            }

            await Task.Delay(TimeSpan.FromSeconds(8));

            SendShowSmallHint(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AP] ShowConnectedToServerAsync error: {ex}");
        }
        finally
        {
            _showConnectionHintRunning = false;
        }
    }

    public void SendSetEventFlagId(int eventId, int value = 0)
    {
        var payload = new
        {
            type = "set_event_flag",
            event_id = eventId,
            value
        };

        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
    }

    public void SendSetEnemyAiDisabled(bool disabled)
    {
        var payload = new
        {
            type = "set_enemy_ai_disabled",
            value = disabled
        };

        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
    }

    public void SendSetOneHitKillEnabled(bool enabled)
    {
        var payload = new
        {
            type = "set_one_hit_kill",
            value = enabled
        };

        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
    }

    public async Task<bool?> SendGetEventFlagIdAsync(int eventId, int timeoutMs = 1500)
    {
        if (eventId <= 0)
            return null;

        int requestId = Interlocked.Increment(ref _eventFlagRequestId);
        var completion = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventFlagRequests[requestId] = completion;

        var payload = new
        {
            type = "get_event_flag",
            request_id = requestId,
            event_id = eventId
        };

        SendJson(JsonSerializer.Serialize(payload));

        var timeout = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
        if (completed == completion.Task)
            return await completion.Task.ConfigureAwait(false);

        _eventFlagRequests.TryRemove(requestId, out _);
        return null;
    }


    public void SendSpawnItem(int goods_id, int quantity, int eventId = 0)
    {
        var request = new SpawnItemRequest
        {
            GoodsId = goods_id,
            Quantity = quantity,
            EventId = eventId
        };

        if (!IsWorldLoaded)
        {
            // Мир ещё не готов — складываем в очередь
            lock (_spawnQueueLock)
            {
                _spawnQueue.Enqueue(request);
            }

            return;
        }

        // Мир уже загружен — отправляем сразу
        SendSpawnItemNow(request);
    }

    private void SendSpawnItemNow(SpawnItemRequest req)
    {
        var payload = new
        {
            type = "grant_item",
            goods_id = req.GoodsId,
            quantity = req.Quantity,
            event_id = req.EventId
        };

        string json = JsonSerializer.Serialize(payload, JsonOptsRelaxed);
        SendJson(json);
    }

    public void SendShowSmallHint(string text)
    {
        if (text.Length > 50)
        {
            text = text.Substring(0, 47) + "...";
        }
        var payload = new
        {
            type = "show_small_hint",
            text
        };

        string json = JsonSerializer.Serialize(payload, JsonOptsRelaxed);
        SendJson(json);
    }

    public void SendShowHint(string text, int headerId = 15100005)
    {
        var payload = new
        {
            type = "show_hint",
            header_id = headerId,
            text = PrepareHintText(text)
        };
        string json = JsonSerializer.Serialize(payload, JsonOptsRelaxed);
        SendJson(json);
    }

    public async Task SendShowSmallHintWhenWorldLoaded(string text, CancellationToken cancellationToken = default)
    {
        // 1. Ждём загрузку мира
        while (!IsWorldLoaded)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await Task.Delay(500, cancellationToken); // проверяем каждые 0.5 сек
        }

        // 2. Дополнительная задержка (экран загрузки)
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // 3. Показываем хинт
        SendShowSmallHint(text);
    }


    public void SendKillPlayer()
    {
        var payload = new
        {
            type = "kill_player"
        };
        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
    }

    public void ChangeDebugState(bool debug)
    {
        var payload = new
        {
            type = "debug_state",
            value = debug
        };
        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
    }

    public void ChangeFullDeathDetection(bool isFullDeathDetection)
    {
        var payload = new
        {
            type = "full_death_detection",
            value = isFullDeathDetection
        };
        string json = JsonSerializer.Serialize(payload);
        SendJson(json);
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

                LogDebug("[Pipe] Waiting for client...");
                await _server.WaitForConnectionAsync(token).ConfigureAwait(false);

                LogDebug("[Pipe] Client connected");

                ConnectionChanged?.Invoke("connected");
                // Когда подключились — запускаем две задачи: чтение и запись
                var readTask = Task.Run(() => ReadLoopAsync(_server, token), token);
                var writeTask = Task.Run(() => WriteLoopAsync(_server, token), token);

                // Ждём, пока одна из задач не завершится (чаще всего из-за дисконнекта)
                await Task.WhenAny(readTask, writeTask).ConfigureAwait(false);

                LogDebug("[Pipe] Client disconnected");
                ConnectionChanged?.Invoke("disconnected");
                IsWorldLoaded = false;
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
                LogDebug("[Pipe] Exception in AcceptLoop: " + ex);
                ConnectionChanged?.Invoke("disconnected");
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
                var jobj = JObject.Parse(json);
                if (jobj.Value<string>("type") == "item_picked")
                {
                    var lotId = jobj.Value<int>("lot_index");
                    var goodId = jobj.Value<int>("goods_id");
                    var quantity = jobj.Value<int?>("quantity") ?? 1;
                    var isfromShop = jobj.Value<bool>("is_from_shop");
                    ItemReceived?.Invoke(new ItemRecievedArgs()
                    {
                        LotId = lotId,
                        GoodId = goodId,
                        Quantity = quantity,
                        IsFromShop = isfromShop
                    });
                }
                else if (jobj.Value<string>("type") == "event_flag_response")
                {
                    HandleEventFlagResponse(jobj);
                }
                else if (jobj.Value<string>("type") == "world")
                {
                    IsWorldLoaded = jobj.Value<bool>("status");
                    WorldStateChanged?.Invoke(IsWorldLoaded);
                }
                else if (jobj.Value<string>("type") == "death")
                {
                    PlayerDeath?.Invoke(true);
                }
                else if (jobj.Value<string>("type") == "ending")
                {
                    var endingType = jobj.Value<string>("status");
                    EndingDetected?.Invoke(endingType ?? "");
                }
            }
            catch (Exception ex)
            {
                LogDebug("[Pipe] MessageReceived handler error: " + ex);
            }
        }
    }

    private void HandleEventFlagResponse(JObject jobj)
    {
        int requestId = jobj.Value<int?>("request_id") ?? -1;
        if (requestId <= 0 || !_eventFlagRequests.TryRemove(requestId, out var completion))
            return;

        bool? value = null;
        if (jobj.TryGetValue("ok", out var okToken) && okToken.Type == JTokenType.Boolean && !okToken.Value<bool>())
        {
            completion.TrySetResult(null);
            return;
        }

        if (jobj.TryGetValue("is_set", out var isSetToken))
        {
            value = isSetToken.Value<bool>();
        }
        else if (jobj.TryGetValue("value", out var valueToken))
        {
            value = valueToken.Value<int>() != 0;
        }
        else if (jobj.TryGetValue("status", out var statusToken) && statusToken.Type != JTokenType.Null)
        {
            value = statusToken.Type == JTokenType.Boolean
                ? statusToken.Value<bool>()
                : statusToken.Value<int>() != 0;
        }

        completion.TrySetResult(value);
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
                    LogDebug("[Pipe] Error in WriteLoop: " + ex);
                    return;
                }
            }
        }
    }

    private void CloseServer()
    {
        try
        {
            IsStarted = false;
            _server?.Dispose();
        }
        catch { /* ignore */ }
        _server = null;
    }

    private void LogDebug(string message)
    {
        Console.WriteLine(message);
        if (ShowDebugLog)
        {
            DebugLodReceived?.Invoke("[Debug]" + message);
        }
    }
    private sealed class SpawnItemRequest
    {
        public int GoodsId { get; init; }
        public int Quantity { get; init; }
        public int EventId { get; init; }
    }

    private void ScheduleSpawnQueueFlush()
    {
        // Уже висит отложенный флеш — не запускаем ещё один
        if (_flushScheduled)
            return;

        _flushScheduled = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8));

                FlushSpawnQueue();
            }
            finally
            {
                _flushScheduled = false;
            }
        });
    }

    private async void FlushSpawnQueue()
    {
        List<SpawnItemRequest> pending;

        lock (_spawnQueueLock)
        {
            if (_spawnQueue.Count == 0)
                return;

            pending = new List<SpawnItemRequest>(_spawnQueue.Count);
            while (_spawnQueue.Count > 0)
                pending.Add(_spawnQueue.Dequeue());
        }

        foreach (var req in pending)
        {
            SendSpawnItemNow(req);
            await Task.Delay(50);
        }
    }

    private static string PrepareHintText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        const int maxLineLength = 35;
        const int maxTotalLength = 128;

        // 1) Нормализуем переносы, но НЕ удаляем их
        input = input.Replace("\r\n", "\n");

        var inputLines = input.Split('\n');
        var resultLines = new List<string>();

        foreach (var line in inputLines)
        {
            if (line.Length <= maxLineLength)
            {
                resultLines.Add(line);
                continue;
            }

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentLine = "";

            foreach (var word in words)
            {
                if (currentLine.Length == 0)
                {
                    currentLine = word;
                }
                else if (currentLine.Length + 1 + word.Length <= maxLineLength)
                {
                    currentLine += " " + word;
                }
                else
                {
                    resultLines.Add(currentLine);
                    currentLine = word;
                }
            }

            if (currentLine.Length > 0)
                resultLines.Add(currentLine);
        }

        string result = string.Join("\n", resultLines);

        if (result.Length > maxTotalLength)
        {
            result = result.Substring(0, maxTotalLength - 3).TrimEnd();
            result += "...";
        }

        return result;
    }
}


public class ItemRecievedArgs
{
    public int LotId { get; set; }

    public int GoodId { get; set; }

    public int Quantity { get; set; }

    public bool IsFromShop { get; set; }
}
