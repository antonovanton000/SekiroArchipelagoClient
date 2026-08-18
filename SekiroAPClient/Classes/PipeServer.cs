// PipeServer.cs
using Newtonsoft.Json.Linq;
using SekiroAPClient.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
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
    private enum TransportKind
    {
        NamedPipe,
        Tcp
    }

    private readonly string _pipeName;
    private readonly TransportKind _transportKind;
    private readonly int _tcpPort;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _server;
    private TcpListener? _tcpListener;
    private TcpClient? _tcpClient;
    private Task? _acceptLoopTask;
    private readonly ConcurrentQueue<string> _sendQueue = new();
    private readonly AutoResetEvent _sendEvent = new(false);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool?>> _eventFlagRequests = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _grantItemRequests = new();
    private int _eventFlagRequestId;
    private int _grantItemRequestId;
    public bool IsStarted { get; private set; }

    public bool IsConnected => _transportKind == TransportKind.Tcp
        ? _tcpClient is { Connected: true }
        : _server is { IsConnected: true };
    public bool IsTcpTransport => _transportKind == TransportKind.Tcp;
    public bool ShowDebugLog { get; set; } = false;

    public bool IsWorldLoaded
    {
        get => _isWorldLoaded;
        set
        {
            if (_isWorldLoaded == value)
                return;

            _isWorldLoaded = value;

            // The world has just loaded; schedule a flush in 8 seconds.
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
    /// Raised when a JSON message is received from the DLL.
    /// </summary>
    public event Action<string>? MessageReceived;
    public event Action<ItemRecievedArgs>? ItemReceived;
    public event Action<string>? DebugLodReceived;
    public event Action<string>? ConnectionChanged;
    public event Action<bool>? WorldStateChanged;
    public event Action<bool>? PlayerDeath;
    public event Action<string>? EndingDetected;
    public event Action<int, bool>? ItemGrantDelivered;


    static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), // Or use UnsafeRelaxedJsonEscaping below.
    };

    static readonly JsonSerializerOptions JsonOptsRelaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PipeServer(string pipeName)
    {
        _pipeName = pipeName;
        _transportKind = ResolveTransportKind();
        _tcpPort = ResolveTcpPort();
        IsStarted = false;
    }

    private static TransportKind ResolveTransportKind()
    {
        var value = Environment.GetEnvironmentVariable("SEKIRO_AP_TRANSPORT");
        if (string.Equals(value, "tcp", StringComparison.OrdinalIgnoreCase))
            return TransportKind.Tcp;

        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connection_transport.txt");
        if (File.Exists(configPath))
        {
            var text = File.ReadLines(configPath).FirstOrDefault()?.Trim();
            if (string.Equals(text, "tcp", StringComparison.OrdinalIgnoreCase))
                return TransportKind.Tcp;
            if (string.Equals(text, "pipe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "namedpipe", StringComparison.OrdinalIgnoreCase))
                return TransportKind.NamedPipe;
        }

        return TransportKind.NamedPipe;
    }

    private static int ResolveTcpPort()
    {
        var value = Environment.GetEnvironmentVariable("SEKIRO_AP_TCP_PORT");
        if (int.TryParse(value, out var port) && port > 0 && port <= 65535)
            return port;

        return 38571;
    }

    public void Start()
    {
        // Start a background task that:
        // - waits for a connection
        // - reads from and writes to this pipe after connecting
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
    /// Queue a JSON string for sending.
    /// It will be sent as soon as a connection is available.
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


    public void SendSpawnItem(int goods_id, int quantity, int eventId = 0, int deliveryFlagId = 0)
    {
        var request = new SpawnItemRequest
        {
            GoodsId = goods_id,
            Quantity = quantity,
            EventId = eventId,
            DeliveryFlagId = deliveryFlagId
        };

        if (!IsWorldLoaded)
        {
            // The world is not ready yet; queue the request.
            lock (_spawnQueueLock)
            {
                _spawnQueue.Enqueue(request);
            }

            return;
        }

        // The world is already loaded; send immediately.
        SendSpawnItemNow(request);
    }

    public async Task<bool> SendSpawnItemReliableAsync(
        int goodsId,
        int quantity,
        int eventId = 0,
        int timeoutMs = 120_000,
        CancellationToken cancellationToken = default)
    {
        if (_cts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
            return false;

        int requestId = Interlocked.Increment(ref _grantItemRequestId);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _grantItemRequests[requestId] = completion;

        var request = new SpawnItemRequest
        {
            GoodsId = goodsId,
            Quantity = quantity,
            EventId = eventId,
            GrantRequestId = requestId
        };

        if (!IsWorldLoaded)
        {
            lock (_spawnQueueLock)
            {
                _spawnQueue.Enqueue(request);
            }
        }
        else
        {
            SendSpawnItemNow(request);
        }

        try
        {
            var timeout = Task.Delay(timeoutMs, cancellationToken);
            var completed = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
            if (completed == completion.Task)
                return await completion.Task.ConfigureAwait(false);

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            _grantItemRequests.TryRemove(requestId, out _);
        }
    }

    private void SendSpawnItemNow(SpawnItemRequest req)
    {
        var payload = new
        {
            type = "grant_item",
            goods_id = req.GoodsId,
            quantity = req.Quantity,
            event_id = req.EventId,
            delivery_flag_id = req.DeliveryFlagId,
            grant_request_id = req.GrantRequestId
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
        // 1. Wait until the world is loaded.
        while (!IsWorldLoaded)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await Task.Delay(500, cancellationToken); // Poll every 0.5 seconds.
        }

        // 2. Extra delay for the loading screen.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // 3. Show the hint.
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
                Stream stream;
                Func<bool> isConnected;

                if (_transportKind == TransportKind.Tcp)
                {
                    _tcpListener ??= new TcpListener(IPAddress.Loopback, _tcpPort);
                    _tcpListener.Start(1);

                    LogDebug($"[Pipe] Waiting for TCP client on 127.0.0.1:{_tcpPort}...");
                    _tcpClient = await _tcpListener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    _tcpClient.NoDelay = true;
                    stream = _tcpClient.GetStream();
                    isConnected = () => _tcpClient is { Connected: true };
                }
                else
                {
                    _server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    LogDebug("[Pipe] Waiting for client...");
                    await _server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    stream = _server;
                    isConnected = () => _server is { IsConnected: true };
                }

                LogDebug("[Pipe] Client connected");

                ConnectionChanged?.Invoke("connected");
                var readTask = Task.Run(() => ReadLoopAsync(stream, isConnected, token), token);
                var writeTask = Task.Run(() => WriteLoopAsync(stream, isConnected, token), token);

                // Wait until either task completes, usually because of a disconnect.
                await Task.WhenAny(readTask, writeTask).ConfigureAwait(false);

                LogDebug("[Pipe] Client disconnected");
                ConnectionChanged?.Invoke("disconnected");
                IsWorldLoaded = false;
                CloseServer();

                // Small delay before the next attempt.
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
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

    private async Task ReadLoopAsync(Stream pipe, Func<bool> isConnected, CancellationToken token)
    {
        var lengthBuffer = new byte[4];

        while (!token.IsCancellationRequested && isConnected())
        {
            // Read the 4-byte length prefix.
            if (!await ReadExactAsync(pipe, lengthBuffer, 0, 4, token).ConfigureAwait(false))
            {
                // Disconnect or read error.
                break;
            }

            uint length = BitConverter.ToUInt32(lengthBuffer, 0);
            if (length == 0)
            {
                // Empty message; skip it.
                continue;
            }

            var payload = new byte[length];
            if (!await ReadExactAsync(pipe, payload, 0, (int)length, token).ConfigureAwait(false))
            {
                break;
            }

            string json = Encoding.UTF8.GetString(payload);

            // Notify UI/log subscribers.
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
                else if (jobj.Value<string>("type") == "grant_item_ack")
                {
                    int deliveryFlagId = jobj.Value<int?>("delivery_flag_id") ?? 0;
                    int grantRequestId = jobj.Value<int?>("grant_request_id") ?? 0;
                    bool delivered = jobj.Value<bool?>("delivered") ?? false;
                    if (grantRequestId > 0 && _grantItemRequests.TryRemove(grantRequestId, out var completion))
                        completion.TrySetResult(delivered);

                    if (deliveryFlagId > 0)
                        ItemGrantDelivered?.Invoke(deliveryFlagId, delivered);
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
                // Client disconnected.
                return false;
            }
            totalRead += read;
        }
        return true;
    }

    private async Task WriteLoopAsync(Stream pipe, Func<bool> isConnected, CancellationToken token)
    {
        while (!token.IsCancellationRequested && isConnected())
        {
            // Wait until messages appear in the queue.
            if (_sendQueue.IsEmpty)
            {
                // Wait for a send signal, or exit if cancellation is requested.
                WaitHandle.WaitAny(new WaitHandle[] { _sendEvent, token.WaitHandle });
                if (token.IsCancellationRequested || !isConnected())
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
                    // Client disconnected.
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
            _tcpClient?.Dispose();
        }
        catch { /* ignore */ }
        _server = null;
        _tcpClient = null;
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
        public int DeliveryFlagId { get; init; }
        public int GrantRequestId { get; init; }
    }

    private void ScheduleSpawnQueueFlush()
    {
        // A delayed flush is already pending; do not start another one.
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
            if (req.GrantRequestId > 0 && !_grantItemRequests.ContainsKey(req.GrantRequestId))
                continue;

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

        // 1) Normalize line endings without removing them.
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
