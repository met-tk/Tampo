#if TAMPO_SYNC_SERVICE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.SyncService
{
    public class PairingRequest
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("pairingCode")]
        public string? PairingCode { get; set; }

        [JsonPropertyName("deviceName")]
        public string? DeviceName { get; set; }

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; set; }

        [JsonPropertyName("deviceModel")]
        public string? DeviceModel { get; set; }

        public string GetEffectiveCode() => !string.IsNullOrWhiteSpace(Code)
            ? Code.Trim()
            : (PairingCode?.Trim() ?? string.Empty);
    }

    /// <summary>
    /// 基于原生 TcpListener 实现的轻量级无特权 HTTP 同步服务 (<30MB 内存约束，杜绝 http.sys 400 Invalid Hostname)
    /// </summary>
    public sealed class HttpSyncServer : IAsyncDisposable, IDisposable
    {
        public const int DefaultPort = 52080;
        public const string ServerVersion = "1.5.3";
        public int Port { get; }

        private TcpListener? _tcpListener;
        private readonly DatabaseService _dbService;
        private readonly CancellationTokenSource _shutdownCts;
        private CancellationTokenSource? _serverCts;
        private Task? _listenTask;
        private static volatile bool _isMaintenanceLocked = false;
        private readonly ConcurrentDictionary<string, (int Attempts, DateTime WindowStartUtc, DateTime LockedUntilUtc)> _pairingAttempts = new();

        private sealed class SseClient : IAsyncDisposable, IDisposable
        {
            public Guid Id { get; } = Guid.NewGuid();
            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public SemaphoreSlim WriteLock { get; } = new(1, 1);
            public CancellationTokenSource DisconnectCts { get; } = new();
            public string? Token { get; }
            public string? DeviceId { get; set; }
            public string? DeviceName { get; set; }
            public string? DeviceModel { get; set; }
            public string? ClientIp { get; set; }
            public DateTime ConnectedAt { get; } = DateTime.UtcNow;

            public SseClient(TcpClient client, NetworkStream stream, string? token = null)
            {
                Client = client;
                Stream = stream;
                Token = token;
            }

            public async Task SendRawAsync(byte[] bytes, CancellationToken ct)
            {
                await WriteLock.WaitAsync(ct);
                try
                {
                    await Stream.WriteAsync(bytes, 0, bytes.Length, ct);
                    await Stream.FlushAsync(ct);
                }
                catch
                {
                    try { DisconnectCts.Cancel(); } catch { }
                    throw;
                }
                finally
                {
                    WriteLock.Release();
                }
            }

            public void Dispose()
            {
                try { DisconnectCts.Cancel(); } catch { }
                try { Stream.Dispose(); } catch { }
                try { Client.Dispose(); } catch { }
                try { WriteLock.Dispose(); } catch { }
                try { DisconnectCts.Dispose(); } catch { }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private readonly ConcurrentDictionary<Guid, SseClient> _sseClients = new();
        private Task? _heartbeatTask;
        private bool _disposed;

        private static readonly JsonSerializerOptions _incomingJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new FlexibleBoolConverter() }
        };

        private static readonly JsonSerializerOptions _outgoingJsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public bool IsRunning => _tcpListener != null;

        public HttpSyncServer(DatabaseService dbService, CancellationTokenSource shutdownCts, int port = DefaultPort)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            _shutdownCts = shutdownCts ?? throw new ArgumentNullException(nameof(shutdownCts));
            Port = port;
            _tcpListener = null;
        }

        public void Start()
        {
            if (IsRunning) return;

            _serverCts = new CancellationTokenSource();
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, Port);
                _tcpListener.Start();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"HttpSyncServer.Start failed on port {Port}");
                throw new InvalidOperationException($"无法在端口 {Port} 上启动 HttpSyncServer，请检查端口占用情况。", ex);
            }

            CrashLogger.LogInfo($"HttpSyncServer started successfully on 0.0.0.0:{Port}");
            _listenTask = Task.Run(ListenLoopAsync);
            _heartbeatTask = Task.Run(() => SendHeartbeatLoopAsync(_serverCts.Token), _serverCts.Token);
        }

        private async Task SendHeartbeatLoopAsync(CancellationToken token)
        {
            byte[] keepAliveBytes = Encoding.UTF8.GetBytes(": keep-alive\n\n");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), token);
                    if (_sseClients.IsEmpty) continue;

                    var deadClients = new List<Guid>();
                    foreach (var kvp in _sseClients)
                    {
                        try
                        {
                            using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            await kvp.Value.SendRawAsync(keepAliveBytes, writeCts.Token);
                        }
                        catch
                        {
                            deadClients.Add(kvp.Key);
                        }
                    }

                    foreach (var id in deadClients)
                    {
                        if (_sseClients.TryRemove(id, out var deadClient))
                        {
                            deadClient.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "HttpSyncServer.SendHeartbeatLoopAsync");
                }
            }
        }

        public async Task BroadcastDataChangedAsync(long dataVersion, string? excludeToken = null)
        {
            if (_sseClients.IsEmpty) return;

            string syncEpoch = "epoch_default";
            try { syncEpoch = await _dbService.GetSyncEpochAsync(); } catch { }

            var payload = new
            {
                @event = "data_changed",
                version = dataVersion,
                syncEpoch = syncEpoch,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            string json = JsonSerializer.Serialize(payload, _outgoingJsonOptions);
            string message = $"event: data_changed\ndata: {json}\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            var deadClients = new List<Guid>();
            foreach (var kvp in _sseClients)
            {
                if (!string.IsNullOrEmpty(excludeToken) && kvp.Value.Token == excludeToken)
                {
                    continue; // 排除发起本次推送的来源设备，杜绝乒乓无限回音
                }

                try
                {
                    using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await kvp.Value.SendRawAsync(bytes, writeCts.Token);
                }
                catch
                {
                    deadClients.Add(kvp.Key);
                }
            }

            foreach (var id in deadClients)
            {
                if (_sseClients.TryRemove(id, out var deadClient))
                {
                    deadClient.Dispose();
                }
            }
        }

        public async Task<int> BroadcastRequestPushAsync(string? targetDeviceId = null)
        {
            if (_sseClients.IsEmpty) return 0;

            var payload = new
            {
                @event = "request_push",
                mode = "full",
                targetDeviceId = targetDeviceId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            string json = JsonSerializer.Serialize(payload, _outgoingJsonOptions);
            string message = $"event: request_push\ndata: {json}\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            int sentCount = 0;
            var deadClients = new List<Guid>();
            foreach (var kvp in _sseClients)
            {
                if (!string.IsNullOrWhiteSpace(targetDeviceId) &&
                    !string.Equals(kvp.Value.DeviceId, targetDeviceId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 定向推送过滤
                }

                try
                {
                    using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await kvp.Value.SendRawAsync(bytes, writeCts.Token);
                    sentCount++;
                }
                catch
                {
                    deadClients.Add(kvp.Key);
                }
            }

            foreach (var id in deadClients)
            {
                if (_sseClients.TryRemove(id, out var deadClient))
                {
                    deadClient.Dispose();
                }
            }

            return sentCount;
        }

        private async Task HandleSseStreamAsync(
            TcpClient client,
            NetworkStream stream,
            string token,
            PairedDevice? device,
            string? clientIp,
            string? queryString,
            CancellationToken serverToken)
        {
            var sseClient = new SseClient(client, stream, token)
            {
                DeviceId = device?.Id,
                DeviceName = device?.DeviceName,
                DeviceModel = device?.DeviceModel,
                ClientIp = clientIp
            };
            if (!string.IsNullOrWhiteSpace(queryString))
            {
                var qParams = ParseQueryString(queryString);
                if (qParams.TryGetValue("deviceId", out var did) && !string.IsNullOrWhiteSpace(did))
                    sseClient.DeviceId = did.Trim();
                if (qParams.TryGetValue("deviceName", out var dname) && !string.IsNullOrWhiteSpace(dname))
                    sseClient.DeviceName = dname.Trim();
                if (qParams.TryGetValue("deviceModel", out var dmodel) && !string.IsNullOrWhiteSpace(dmodel))
                    sseClient.DeviceModel = dmodel.Trim();
            }
            _sseClients[sseClient.Id] = sseClient;

            try
            {
                var headers = "HTTP/1.1 200 OK\r\n" +
                              "Content-Type: text/event-stream; charset=utf-8\r\n" +
                              "Cache-Control: no-cache, no-transform\r\n" +
                              "Connection: keep-alive\r\n" +
                              "Access-Control-Allow-Origin: *\r\n" +
                              "Access-Control-Allow-Methods: GET, OPTIONS\r\n" +
                              "Access-Control-Allow-Headers: Content-Type, Authorization, X-Sync-Token\r\n\r\n";
                byte[] headerBytes = Encoding.UTF8.GetBytes(headers);
                await sseClient.SendRawAsync(headerBytes, CancellationToken.None);

                long currentVersion = 0;
                try
                {
                    currentVersion = await _dbService.GetDataVersionAsync();
                }
                catch { }

                string syncEpoch = "epoch_default";
                try
                {
                    syncEpoch = await _dbService.GetSyncEpochAsync();
                }
                catch { }

                var connectData = new
                {
                    @event = "connected",
                    version = currentVersion,
                    syncEpoch = syncEpoch,
                    serverTime = DateTime.UtcNow,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                string initEvent = $"event: connected\ndata: {JsonSerializer.Serialize(connectData, _outgoingJsonOptions)}\n\n";
                await sseClient.SendRawAsync(Encoding.UTF8.GetBytes(initEvent), CancellationToken.None);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken, sseClient.DisconnectCts.Token);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CrashLogger.LogInfo($"SSE client ({sseClient.Id}) disconnected: {ex.Message}");
            }
            finally
            {
                _sseClients.TryRemove(sseClient.Id, out _);
                sseClient.Dispose();
            }
        }

        private async Task ListenLoopAsync()
        {
            var token = _serverCts?.Token ?? CancellationToken.None;

            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleTcpClientAsync(client, token));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    CrashLogger.LogException(ex, "HttpSyncServer.ListenLoopAsync");
                    await Task.Delay(50, token);
                }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                var reqToken = timeoutCts.Token;

                try
                {
                    stream.ReadTimeout = 15000;
                    stream.WriteTimeout = 15000;

                    // 1. 异步读取 HTTP 请求报文头
                    const int MaxHeaderBytes = 65536;
                    byte[] readBuf = new byte[4096];
                    using var headerBuffer = new MemoryStream();
                    int headerEndIndex = -1;

                    while (headerEndIndex < 0)
                    {
                        int n = await stream.ReadAsync(readBuf, 0, readBuf.Length, reqToken);
                        if (n <= 0) break;

                        headerBuffer.Write(readBuf, 0, n);
                        headerEndIndex = FindHeaderEnd(headerBuffer.GetBuffer(), (int)headerBuffer.Length);

                        if (headerEndIndex < 0 && headerBuffer.Length > MaxHeaderBytes)
                        {
                            await SendHttpResponseAsync(stream, 400, "Bad Request", Encoding.UTF8.GetBytes("Header Too Large"));
                            return;
                        }
                    }

                    if (headerEndIndex < 0) return;

                    byte[] rawBuf = headerBuffer.GetBuffer();
                    int totalBuffered = (int)headerBuffer.Length;
                    int bodyStartOffset = headerEndIndex + 4;

                    string headerString = Encoding.UTF8.GetString(rawBuf, 0, headerEndIndex);
                    var headerLines = headerString.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (headerLines.Length == 0) return;

                    var requestLineParts = headerLines[0].Split(' ');
                    if (requestLineParts.Length < 2) return;

                    string method = requestLineParts[0].ToUpperInvariant();
                    string rawUrl = requestLineParts[1];

                    // 解析 path 与 query string
                    string path = rawUrl;
                    string? queryString = null;
                    int qIndex = rawUrl.IndexOf('?');
                    if (qIndex >= 0)
                    {
                        path = rawUrl.Substring(0, qIndex);
                        queryString = rawUrl.Substring(qIndex + 1);
                    }

                    // 解析 Headers
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < headerLines.Length; i++)
                    {
                        int colonIdx = headerLines[i].IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string hKey = headerLines[i].Substring(0, colonIdx).Trim();
                            string hVal = headerLines[i].Substring(colonIdx + 1).Trim();
                            headers[hKey] = hVal;
                        }
                    }

                    // 仅在路由完成鉴权后读取请求体，避免匿名连接分配大块内存。
                    long contentLength = headers.TryGetValue("Content-Length", out var clStr) && long.TryParse(clStr, out var clVal)
                        ? clVal : -1;
                    var remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
                    var remoteIp = remoteEndPoint?.Address;

                    // CORS OPTIONS 预检请求
                    if (method == "OPTIONS")
                    {
                        await SendHttpResponseAsync(stream, 204, "No Content", Array.Empty<byte>());
                        return;
                    }

                    // 1. GET /api/ping
                    if (path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase) && method == "GET")
                    {
                        string syncEpoch = "epoch_default";
                        try { syncEpoch = await _dbService.GetSyncEpochAsync(); } catch { }

                        var pingObj = new
                        {
                            status = "ok",
                            service = "TampoSyncService",
                            version = ServerVersion,
                            name = Environment.MachineName,
                            syncEpoch = syncEpoch,
                            server_time = DateTime.UtcNow,
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        await SendHttpJsonAsync(stream, 200, "OK", pingObj, reqToken);
                        return;
                    }

                    // 2. POST /api/pair
                    if (path.Equals("/api/pair", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        string ipKey = remoteIp?.ToString() ?? "unknown";
                        if (_pairingAttempts.Count >= 1024)
                        {
                            PrunePairingAttempts();
                            if (_pairingAttempts.Count >= 1024 && !_pairingAttempts.ContainsKey(ipKey))
                            {
                                await SendHttpJsonAsync(stream, 429, "Too Many Requests", new { error = "Pairing temporarily unavailable" }, reqToken);
                                return;
                            }
                        }
                        if (IsPairingRateLimited(ipKey))
                        {
                            await SendHttpJsonAsync(stream, 429, "Too Many Requests", new { error = "Pairing temporarily locked" }, reqToken);
                            return;
                        }

                        if (contentLength <= 0 || contentLength > 4096)
                        {
                            await SendHttpJsonAsync(stream, contentLength > 4096 ? 413 : 400,
                                contentLength > 4096 ? "Payload Too Large" : "Bad Request", new { error = "Invalid pairing request size" }, reqToken);
                            return;
                        }

                        byte[]? pairBytes = await ReadBodyAsync(stream, rawBuf, bodyStartOffset, totalBuffered, (int)contentLength, reqToken);
                        if (pairBytes == null)
                        {
                            await SendHttpJsonAsync(stream, 400, "Bad Request", new { error = "Incomplete pairing request" }, reqToken);
                            return;
                        }
                        PairingRequest? pairReq = null;
                        try
                        {
                            pairReq = JsonSerializer.Deserialize<PairingRequest>(pairBytes, _incomingJsonOptions);
                        }
                        catch { }

                        string code = pairReq?.GetEffectiveCode() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Invalid or expired pairing code" }, reqToken);
                            return;
                        }

                        bool valid = await _dbService.ValidateAndConsumePairingCodeAsync(code);
                        if (!valid)
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Invalid or expired pairing code" }, reqToken);
                            return;
                        }
                        _pairingAttempts.TryRemove(ipKey, out _);

                        // 生成 256 位高强度安全 Hex Token (32 字节 -> 64 字符十六进制)
                        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
                        string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();

                        var device = new PairedDevice
                        {
                            Id = !string.IsNullOrWhiteSpace(pairReq?.DeviceId) ? pairReq!.DeviceId!.Trim() : Guid.NewGuid().ToString(),
                            DeviceName = !string.IsNullOrWhiteSpace(pairReq?.DeviceName) ? pairReq!.DeviceName!.Trim() : "Mobile Device",
                            DeviceModel = !string.IsNullOrWhiteSpace(pairReq?.DeviceModel) ? pairReq!.DeviceModel!.Trim() : null,
                            AuthToken = token,
                            ClientIp = remoteIp?.ToString(),
                            LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        };

                        await _dbService.SavePairedDeviceAsync(device);

                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "paired",
                            token = token,
                            message = "Pairing successful"
                        }, reqToken);
                        return;
                    }

                    // 2.5 GET /api/sync/events (轻量级 Server-Sent Events 下行广播)
                    if (path.Equals("/api/sync/events", StringComparison.OrdinalIgnoreCase) && method == "GET")
                    {
                        string? token = ExtractAuthToken(headers, queryString);
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized: Missing Token" }, reqToken);
                            return;
                        }

                        var device = await _dbService.GetDeviceByTokenAsync(token);
                        if (device == null)
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized: Invalid Token" }, reqToken);
                            return;
                        }

                        string clientIp = remoteIp?.ToString() ?? string.Empty;
                        var queryDict = ParseQueryString(queryString);
                        string? queryModel = queryDict.TryGetValue("deviceModel", out var qm) ? qm : (queryDict.TryGetValue("model", out var qm2) ? qm2 : null);
                        await _dbService.UpdateDeviceLastSeenAsync(token, clientIp, queryModel);

                        // 解除 30 秒超时限制，切换为长连接流
                        timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        stream.ReadTimeout = Timeout.Infinite;

                        await HandleSseStreamAsync(client, stream, token, device, clientIp, queryString, ct);
                        return;
                    }

                    // 3. POST /api/sync/pull (拉取数据)
                    if (path.Equals("/api/sync/pull", StringComparison.OrdinalIgnoreCase) && (method == "POST" || method == "GET"))
                    {
                        if (_isMaintenanceLocked)
                        {
                            await SendHttpJsonAsync(stream, 503, "Service Unavailable", new { error = "maintenance_in_progress", message = "电脑端正在进行数据维护与备份还原，同步暂时锁定，请稍后重试。" }, reqToken);
                            return;
                        }

                        string? token = ExtractAuthToken(headers, queryString);
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized: invalid or revoked token" }, reqToken);
                            return;
                        }

                        var device = await _dbService.GetDeviceByTokenAsync(token);
                        if (device == null)
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized: invalid or revoked token" }, reqToken);
                            return;
                        }

                        string clientIp = remoteIp?.ToString() ?? string.Empty;
                        await _dbService.UpdateDeviceLastSeenAsync(token, clientIp);

                        long? since = ParseSinceFromQuery(queryString);
                        var payload = await BuildSyncPayloadAsync(since);

                        await SendHttpJsonAsync(stream, 200, "OK", payload, reqToken);
                        return;
                    }

                    // 4. POST /api/sync/push (推送数据合并)
                    if (path.Equals("/api/sync/push", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        if (_isMaintenanceLocked)
                        {
                            await SendHttpJsonAsync(stream, 503, "Service Unavailable", new { error = "maintenance_in_progress", message = "电脑端正在进行数据维护与备份还原，同步暂时锁定，请稍后重试。" }, reqToken);
                            return;
                        }

                        string? token = ExtractAuthToken(headers, queryString);
                        if (string.IsNullOrWhiteSpace(token))
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized" }, reqToken);
                            return;
                        }

                        var device = await _dbService.GetDeviceByTokenAsync(token);
                        if (device == null)
                        {
                            await SendHttpJsonAsync(stream, 401, "Unauthorized", new { error = "Unauthorized" }, reqToken);
                            return;
                        }

                        string clientIp = remoteIp?.ToString() ?? string.Empty;
                        await _dbService.UpdateDeviceLastSeenAsync(token, clientIp);

                        const long MaxPayloadBytes = 50 * 1024 * 1024;
                        if (contentLength <= 0 || contentLength > MaxPayloadBytes)
                        {
                            await SendHttpJsonAsync(stream, contentLength > MaxPayloadBytes ? 413 : 400,
                                contentLength > MaxPayloadBytes ? "Payload Too Large" : "Bad Request", new { error = "Invalid sync request size" }, reqToken);
                            return;
                        }

                        byte[]? bodyBytes = await ReadBodyAsync(stream, rawBuf, bodyStartOffset, totalBuffered, (int)contentLength, reqToken);
                        if (bodyBytes == null)
                        {
                            await SendHttpJsonAsync(stream, 400, "Bad Request", new { error = "Incomplete request body" }, reqToken);
                            return;
                        }

                        SyncPayload? incomingPayload = null;
                        try
                        {
                            incomingPayload = JsonSerializer.Deserialize<SyncPayload>(bodyBytes, _incomingJsonOptions);
                        }
                        catch (Exception ex)
                        {
                            CrashLogger.LogException(ex, "HttpSyncServer.DeserializePushPayload");
                        }

                        if (incomingPayload == null)
                        {
                            await SendHttpJsonAsync(stream, 400, "Bad Request", new { error = "Invalid JSON payload" }, reqToken);
                            return;
                        }

                        string currentEpoch = await _dbService.GetSyncEpochAsync();
                        if (!string.IsNullOrWhiteSpace(currentEpoch))
                        {
                            if (string.IsNullOrWhiteSpace(incomingPayload.SyncEpoch) ||
                                !string.Equals(incomingPayload.SyncEpoch, currentEpoch, StringComparison.Ordinal))
                            {
                                await SendHttpJsonAsync(stream, 409, "Conflict", new
                                {
                                    error = "epoch_mismatch",
                                    message = "电脑端已重置同步纪元，请先执行权威全量拉取以对齐最新数据",
                                    syncEpoch = currentEpoch
                                }, reqToken);
                                return;
                            }
                        }

                        var result = await MergeSyncPayloadAsync(incomingPayload);

                        bool hasActualChanges = (result.SyncedWords > 0 || result.SyncedLogs > 0 || result.SyncedLists > 0);

                        // 仅当实际合并了数据变动时，才发送跨进程信号通知主程序 UI 刷新
                        // 严禁在 0 变动的空请求时误发信号，彻底根除桌面端 UI 抽动
                        if (hasActualChanges)
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                CrossProcessEventHelper.Signal(CrossProcessEventHelper.ToAppEventName);
                                CrossProcessEventHelper.Signal(CrossProcessEventHelper.EventName);
                            }

                            // 向已连接的移动端 SSE 广播数据变动（排除当前推送设备，切断乒乓回音）
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    long ver = await _dbService.GetDataVersionAsync();
                                    await BroadcastDataChangedAsync(ver, excludeToken: token);
                                }
                                catch { }
                            });
                        }

                        // 立即修剪工作集内存，保证常驻 <30MB
                        MemoryOptimizer.TrimWorkingSet();

                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "ok",
                            syncEpoch = currentEpoch,
                            merged = new
                            {
                                words = result.SyncedWords,
                                lists = result.SyncedLists,
                                logs = result.SyncedLogs
                            }
                        }, reqToken);
                        return;
                    }

                    // 4.5 POST /api/sync/request-mobile-push (通知手机端发起全量推送恢复，仅限本机回环调用)
                    if (path.Equals("/api/sync/request-mobile-push", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        if (!IsLocalControlRequest(remoteIp, headers))
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Forbidden: request-mobile-push endpoint is loopback only" }, reqToken);
                            return;
                        }

                        string? targetDeviceId = null;
                        if (!string.IsNullOrWhiteSpace(queryString))
                        {
                            var qParams = ParseQueryString(queryString);
                            if (qParams.TryGetValue("targetDeviceId", out var tid) && !string.IsNullOrWhiteSpace(tid))
                            {
                                targetDeviceId = tid.Trim();
                            }
                        }

                        int notified = await BroadcastRequestPushAsync(targetDeviceId);
                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "ok",
                            notifiedDevices = notified,
                            targetDeviceId = targetDeviceId,
                            message = notified > 0
                                ? (!string.IsNullOrWhiteSpace(targetDeviceId) ? "已向指定目标手机发送全量数据推送请求" : $"已向 {notified} 台连接中的手机发送全量数据推送请求")
                                : "未找到在线连接的目标手机，请先确保手机端处于打开状态并在同一局域网下"
                        }, reqToken);
                        return;
                    }

                    // 4.6 POST /api/control/maintenance-mode (仅限本机回环调用，设置数据维护锁定状态)
                    if (path.Equals("/api/control/maintenance-mode", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        if (!IsLocalControlRequest(remoteIp, headers))
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Forbidden: loopback only" }, reqToken);
                            return;
                        }

                        bool enabled = false;
                        if (!string.IsNullOrWhiteSpace(queryString))
                        {
                            var qParams = ParseQueryString(queryString);
                            if (qParams.TryGetValue("enabled", out var enStr))
                            {
                                bool.TryParse(enStr, out enabled);
                            }
                        }
                        _isMaintenanceLocked = enabled;
                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "ok",
                            maintenanceMode = _isMaintenanceLocked
                        }, reqToken);
                        return;
                    }

                    // 4.7 GET /api/devices/status (仅限本机回环调用，获取已配对设备的实时在线状态与活跃连接)
                    if (path.Equals("/api/devices/status", StringComparison.OrdinalIgnoreCase) && method == "GET")
                    {
                        if (!IsLocalControlRequest(remoteIp, headers))
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Forbidden: loopback only" }, reqToken);
                            return;
                        }

                        var pairedDevices = await _dbService.GetPairedDevicesAsync();
                        var activeSse = _sseClients.Values.ToList();

                        var resultList = new List<object>();
                        foreach (var d in pairedDevices)
                        {
                            var matchingClients = activeSse.Where(c =>
                                (!string.IsNullOrEmpty(d.Id) && string.Equals(c.DeviceId, d.Id, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(d.AuthToken) && string.Equals(c.Token, d.AuthToken, StringComparison.OrdinalIgnoreCase))
                            ).ToList();

                            bool isOnline = matchingClients.Count > 0;
                            string? currentIp = isOnline ? matchingClients[0].ClientIp : d.ClientIp;
                            string? model = !string.IsNullOrWhiteSpace(d.DeviceModel)
                                ? d.DeviceModel
                                : (matchingClients.Count > 0 ? matchingClients[0].DeviceModel : null);

                            resultList.Add(new
                            {
                                id = d.Id,
                                deviceName = d.DeviceName,
                                deviceModel = model,
                                customAlias = d.CustomAlias,
                                clientIp = currentIp,
                                isOnline = isOnline,
                                lastSeenAt = d.LastSeenAt,
                                createdAt = d.CreatedAt
                            });
                        }

                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "ok",
                            devices = resultList
                        }, reqToken);
                        return;
                    }

                    // 5. POST /api/control/stop (仅限本机回环优雅关停)
                    if (path.Equals("/api/control/stop", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        if (!IsLocalControlRequest(remoteIp, headers))
                        {
                            await SendHttpJsonAsync(stream, 403, "Forbidden", new { error = "Forbidden: control endpoints are loopback only" }, reqToken);
                            return;
                        }

                        await SendHttpJsonAsync(stream, 200, "OK", new
                        {
                            status = "stopping",
                            message = "Graceful shutdown initiated"
                        }, reqToken);

                        // 异步延时触发全局关停信号，确保 HTTP 响应完整送达主程序
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            try
                            {
                                _shutdownCts.Cancel();
                            }
                            catch { }
                        });
                        return;
                    }

                    // 404
                    await SendHttpJsonAsync(stream, 404, "Not Found", new { error = "Not Found" }, reqToken);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "HttpSyncServer.HandleTcpClientAsync");
                    try
                    {
                        await SendHttpJsonAsync(stream, 500, "Internal Server Error", new { error = "Internal Server Error", message = ex.Message }, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        private bool IsPairingRateLimited(string ipKey)
        {
            DateTime now = DateTime.UtcNow;
            var state = _pairingAttempts.AddOrUpdate(ipKey,
                _ => (1, now, DateTime.MinValue),
                (_, previous) =>
                {
                    if (now < previous.LockedUntilUtc)
                        return previous;
                    if (now - previous.WindowStartUtc >= TimeSpan.FromMinutes(1))
                        return (1, now, DateTime.MinValue);
                    int attempts = previous.Attempts + 1;
                    return (attempts, previous.WindowStartUtc,
                        attempts > 5 ? now.AddMinutes(1) : DateTime.MinValue);
                });
            return state.Attempts > 5 || now < state.LockedUntilUtc;
        }

        private void PrunePairingAttempts()
        {
            DateTime now = DateTime.UtcNow;
            foreach (var entry in _pairingAttempts)
            {
                if (now - entry.Value.WindowStartUtc >= TimeSpan.FromMinutes(1) &&
                    now >= entry.Value.LockedUntilUtc)
                    _pairingAttempts.TryRemove(entry.Key, out _);
            }
        }

        private static bool IsLocalControlRequest(IPAddress? remoteIp, IReadOnlyDictionary<string, string> headers)
            => remoteIp != null && IPAddress.IsLoopback(remoteIp) &&
               !headers.ContainsKey("Origin") &&
               (!headers.TryGetValue("Sec-Fetch-Site", out var site) ||
                site.Equals("none", StringComparison.OrdinalIgnoreCase));

        private static async Task<byte[]?> ReadBodyAsync(NetworkStream stream, byte[] buffered, int bodyOffset,
            int bufferedLength, int contentLength, CancellationToken ct)
        {
            byte[] body = new byte[contentLength];
            int copied = Math.Min(contentLength, Math.Max(0, bufferedLength - bodyOffset));
            if (copied > 0) Buffer.BlockCopy(buffered, bodyOffset, body, 0, copied);
            while (copied < contentLength)
            {
                int read = await stream.ReadAsync(body, copied, contentLength - copied, ct);
                if (read <= 0) return null;
                copied += read;
            }
            return body;
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i < length - 3; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return i;
            }
            return -1;
        }

        public static string? ExtractAuthToken(IReadOnlyDictionary<string, string> headers, string? queryString)
        {
            if (headers.TryGetValue("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader))
            {
                string trimmed = authHeader.Trim();
                if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    string token = trimmed.Substring(7).Trim();
                    if (!string.IsNullOrEmpty(token)) return token;
                }
                else if (!trimmed.Contains(' '))
                {
                    return trimmed;
                }
            }

            if (headers.TryGetValue("X-Sync-Token", out var syncToken) && !string.IsNullOrWhiteSpace(syncToken))
            {
                return syncToken.Trim();
            }

            if (!string.IsNullOrWhiteSpace(queryString))
            {
                var qParts = queryString.Split('&');
                foreach (var q in qParts)
                {
                    var kv = q.Split('=');
                    if (kv.Length == 2 && kv[0].Equals("token", StringComparison.OrdinalIgnoreCase))
                    {
                        return Uri.UnescapeDataString(kv[1]).Trim();
                    }
                }
            }

            return null;
        }

        public static Dictionary<string, string> ParseQueryString(string? queryString)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(queryString)) return dict;
            var parts = queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var eq = p.IndexOf('=');
                if (eq >= 0)
                {
                    var k = Uri.UnescapeDataString(p.Substring(0, eq));
                    var v = Uri.UnescapeDataString(p.Substring(eq + 1));
                    dict[k] = v;
                }
                else
                {
                    dict[Uri.UnescapeDataString(p)] = string.Empty;
                }
            }
            return dict;
        }

        public static long? ParseSinceFromQuery(string? queryString)
        {
            if (string.IsNullOrWhiteSpace(queryString)) return null;
            var dict = ParseQueryString(queryString);
            if (dict.TryGetValue("since", out var sinceVal))
            {
                return ParseSinceParameter(sinceVal);
            }
            return null;
        }

        public static long? ParseSinceParameter(string? sinceStr)
        {
            if (string.IsNullOrWhiteSpace(sinceStr)) return null;

            if (long.TryParse(sinceStr.Trim(), out long ms))
            {
                return ms;
            }

            if (DateTime.TryParse(sinceStr.Trim(), out DateTime dt))
            {
                return new DateTimeOffset(dt.ToUniversalTime()).ToUnixTimeMilliseconds();
            }

            return null;
        }

        private static async Task SendHttpJsonAsync(NetworkStream stream, int statusCode, string statusDescription, object data, CancellationToken ct)
        {
            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(data, _outgoingJsonOptions);
            await SendHttpResponseAsync(stream, statusCode, statusDescription, jsonBytes, "application/json; charset=utf-8", ct);
        }

        private static async Task SendHttpResponseAsync(NetworkStream stream, int statusCode, string statusDescription, byte[] body, string contentType = "text/plain; charset=utf-8", CancellationToken ct = default)
        {
            var header = new StringBuilder();
            header.Append($"HTTP/1.1 {statusCode} {statusDescription}\r\n");
            header.Append($"Content-Type: {contentType}\r\n");
            header.Append($"Content-Length: {body.Length}\r\n");
            header.Append("Access-Control-Allow-Origin: *\r\n");
            header.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            header.Append("Access-Control-Allow-Headers: Content-Type, Authorization, X-Sync-Token, X-Sync-Pin\r\n");
            header.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.UTF8.GetBytes(header.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, ct);
            if (body.Length > 0)
            {
                await stream.WriteAsync(body, 0, body.Length, ct);
            }
            await stream.FlushAsync(ct);
        }

        private async Task<SyncPayload> BuildSyncPayloadAsync(long? since = null)
        {
            var lists = await _dbService.GetAllWordListsAsync();
            var allWords = await _dbService.GetAllWordsAsync();
            var logs = await _dbService.GetAllReviewLogsAsync();
            var tombstones = await _dbService.GetAllTombstonesAsync();

            var wordMapById = allWords.ToDictionary(w => w.Id, w => w.Text);

            List<Word> words;
            if (since.HasValue && since.Value > 0)
            {
                var sinceTime = DateTimeOffset.FromUnixTimeMilliseconds(since.Value).LocalDateTime;
                lists = lists.Where(l => l.UpdatedAt >= sinceTime || l.CreatedAt >= sinceTime).ToList();
                words = allWords.Where(w => w.MetaUpdatedAt >= sinceTime || w.StateUpdatedAt >= sinceTime || w.CreatedAt >= sinceTime || (w.LastReviewDate.HasValue && w.LastReviewDate.Value >= sinceTime)).ToList();
                logs = logs.Where(l => l.ReviewDate >= sinceTime).ToList();
                tombstones = tombstones.Where(t => t.DeletedAt >= sinceTime).ToList();
            }
            else
            {
                words = allWords;
            }

            foreach (var w in words)
            {
                if (w.StateUpdatedAt == default) w.StateUpdatedAt = w.LastReviewDate ?? w.CreatedAt;
                if (w.MetaUpdatedAt == default) w.MetaUpdatedAt = w.CreatedAt;
            }

            foreach (var l in logs)
            {
                if (wordMapById.TryGetValue(l.WordId, out var wt))
                {
                    l.WordText = wt;
                }
            }

            string syncEpoch = "epoch_default";
            try
            {
                syncEpoch = await _dbService.GetSyncEpochAsync();
            }
            catch { }

            return new SyncPayload
            {
                WordLists = lists,
                Words = words,
                ReviewLogs = logs,
                Tombstones = tombstones,
                Timestamp = DateTime.Now,
                DeviceName = Environment.MachineName,
                SyncEpoch = syncEpoch
            };
        }

        private async Task<SyncResult> MergeSyncPayloadAsync(SyncPayload incoming)
        {
            // 自动快照：在任何合并执行前对数据库进行原子备份，保留近 10 份快照
            try
            {
                await _dbService.CreateAutomaticBackupAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "HttpSyncServer.MergeSyncPayloadAsync.CreateAutomaticBackupAsync");
            }

            using var _ = DatabaseService.SuppressNotifications();

            int updatedWords = 0;
            int insertedLogs = 0;
            int syncedLists = 0;

            // 0. 双向应用删除墓碑 (Tombstones)
            var localTombstones = await _dbService.GetAllTombstonesAsync();
            var deletedWordTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in localTombstones.Where(t => t.EntityType == "word"))
            {
                string k = t.EntityKey.Trim().ToLowerInvariant();
                if (!deletedWordTimes.TryGetValue(k, out var prev) || t.DeletedAt > prev)
                    deletedWordTimes[k] = t.DeletedAt;
            }

            var deletedListTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in localTombstones.Where(t => t.EntityType == "word_list"))
            {
                string k = t.EntityKey.Trim().ToLowerInvariant();
                if (!deletedListTimes.TryGetValue(k, out var prev) || t.DeletedAt > prev)
                    deletedListTimes[k] = t.DeletedAt;
            }

            var deletedLogKeys = new HashSet<string>(
                localTombstones.Where(t => t.EntityType == "review_log").Select(t => t.EntityKey.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            var currentWords = await _dbService.GetAllWordsAsync();

            if (incoming.Tombstones != null && incoming.Tombstones.Count > 0)
            {
                var currentListsNow = await _dbService.GetAllWordListsAsync();
                var currentLogsNow = await _dbService.GetAllReviewLogsAsync();

                var wordIndexByKey = new Dictionary<string, Word>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in currentWords)
                {
                    string k = w.Text.Trim();
                    if (!string.IsNullOrEmpty(k) && !wordIndexByKey.ContainsKey(k))
                        wordIndexByKey[k] = w;
                }

                var listIndexByKey = new Dictionary<string, WordList>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in currentListsNow)
                {
                    string k = l.Name.Trim();
                    if (!string.IsNullOrEmpty(k) && !listIndexByKey.ContainsKey(k))
                        listIndexByKey[k] = l;
                }

                var logIndexByWordAndTime = new Dictionary<string, ReviewLog>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in currentLogsNow)
                {
                    DateTime d = l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate;
                    logIndexByWordAndTime[$"{l.WordId}_{d:yyyyMMddHHmmss}"] = l;
                }

                foreach (var inTomb in incoming.Tombstones)
                {
                    string cleanKey = inTomb.EntityKey.Trim().ToLowerInvariant();
                    DateTime tombDeletedAt = inTomb.DeletedAt.Kind == DateTimeKind.Utc ? inTomb.DeletedAt.ToLocalTime() : inTomb.DeletedAt;
                    if (inTomb.EntityType == "word")
                    {
                        wordIndexByKey.TryGetValue(cleanKey, out var match);
                        DateTime matchTime = match != null
                            ? (match.StateUpdatedAt > match.MetaUpdatedAt
                                ? (match.StateUpdatedAt > match.CreatedAt ? match.StateUpdatedAt : match.CreatedAt)
                                : (match.MetaUpdatedAt > match.CreatedAt ? match.MetaUpdatedAt : match.CreatedAt))
                            : default;

                        if (match == null || tombDeletedAt == default || tombDeletedAt >= matchTime.AddSeconds(-1))
                        {
                            deletedWordTimes[cleanKey] = tombDeletedAt;
                            await _dbService.AddTombstoneAsync("word", cleanKey, tombDeletedAt);

                            if (match != null)
                            {
                                await _dbService.DeleteWordsAsync(new[] { match.Id });
                                wordIndexByKey.Remove(cleanKey);
                            }
                        }
                        else
                        {
                            deletedWordTimes.Remove(cleanKey);
                            await _dbService.RemoveTombstoneAsync("word", cleanKey);
                        }
                    }
                    else if (inTomb.EntityType == "word_list")
                    {
                        listIndexByKey.TryGetValue(cleanKey, out var match);
                        DateTime matchTime = match != null ? (match.UpdatedAt > match.CreatedAt ? match.UpdatedAt : match.CreatedAt) : default;

                        if (match == null || tombDeletedAt == default || tombDeletedAt >= matchTime.AddSeconds(-1))
                        {
                            deletedListTimes[cleanKey] = tombDeletedAt;
                            await _dbService.AddTombstoneAsync("word_list", cleanKey, tombDeletedAt);

                            if (match != null)
                            {
                                await _dbService.DeleteWordListAsync(match.Id);
                                listIndexByKey.Remove(cleanKey);
                            }
                        }
                        else
                        {
                            deletedListTimes.Remove(cleanKey);
                            await _dbService.RemoveTombstoneAsync("word_list", cleanKey);
                        }
                    }
                    else if (inTomb.EntityType == "review_log")
                    {
                        deletedLogKeys.Add(cleanKey);
                        await _dbService.AddTombstoneAsync("review_log", cleanKey, tombDeletedAt);

                        int lastUnderscore = cleanKey.LastIndexOf('_');
                        if (lastUnderscore > 0)
                        {
                            string wText = cleanKey.Substring(0, lastUnderscore);
                            string timeStr = cleanKey.Substring(lastUnderscore + 1);
                            if (wordIndexByKey.TryGetValue(wText, out var matchWord) &&
                                logIndexByWordAndTime.TryGetValue($"{matchWord.Id}_{timeStr}", out var matchLog))
                            {
                                await _dbService.RevertTodayReviewAsync(matchLog.Id, isSyncRevert: true);
                                logIndexByWordAndTime.Remove($"{matchWord.Id}_{timeStr}");
                            }
                        }
                    }
                }
            }

            currentWords = await _dbService.GetAllWordsAsync();
            var currentLists = await _dbService.GetAllWordListsAsync();

            // 1. 同步词单 (WordLists)
            var listMapByName = new Dictionary<string, WordList>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in currentLists)
            {
                string clean = l.Name.Trim();
                if (!string.IsNullOrEmpty(clean) && !listMapByName.ContainsKey(clean))
                {
                    listMapByName[clean] = l;
                }
            }

            foreach (var inList in incoming.WordLists)
            {
                string cleanName = inList.Name.Trim();
                if (string.IsNullOrEmpty(cleanName)) continue;

                string lowerListName = cleanName.ToLowerInvariant();
                if (deletedListTimes.TryGetValue(lowerListName, out var listDelTime))
                {
                    DateTime inListTime = inList.UpdatedAt > inList.CreatedAt ? inList.UpdatedAt : inList.CreatedAt;
                    if (listDelTime == default || listDelTime >= inListTime.AddSeconds(-1))
                    {
                        continue;
                    }
                    deletedListTimes.Remove(lowerListName);
                    await _dbService.RemoveTombstoneAsync("word_list", lowerListName);
                }

                if (!listMapByName.TryGetValue(cleanName, out var localList))
                {
                    var newList = await _dbService.CreateWordListAsync(cleanName);
                    newList.UpdatedAt = inList.UpdatedAt;
                    await _dbService.UpdateWordListAsync(newList);
                    listMapByName[cleanName] = newList;
                    syncedLists++;
                }
                else
                {
                    if (inList.UpdatedAt > localList.UpdatedAt.AddSeconds(1))
                    {
                        localList.UpdatedAt = inList.UpdatedAt;
                        await _dbService.UpdateWordListAsync(localList);
                    }
                }
            }

            // 2. 双向正交合并单词 (Words)
            var currentWordMap = new Dictionary<string, Word>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in currentWords)
            {
                string clean = w.Text.Trim();
                if (!string.IsNullOrEmpty(clean) && !currentWordMap.ContainsKey(clean))
                {
                    currentWordMap[clean] = w;
                }
            }

            foreach (var inWord in incoming.Words)
            {
                string text = inWord.Text.Trim();
                if (string.IsNullOrEmpty(text)) continue;

                if (inWord.StateUpdatedAt == default) inWord.StateUpdatedAt = inWord.LastReviewDate ?? inWord.CreatedAt;
                if (inWord.MetaUpdatedAt == default) inWord.MetaUpdatedAt = inWord.CreatedAt;

                string lowerWordKey = text.ToLowerInvariant();
                if (deletedWordTimes.TryGetValue(lowerWordKey, out var wordDelTime))
                {
                    DateTime inWordTime = inWord.MetaUpdatedAt > inWord.CreatedAt ? inWord.MetaUpdatedAt : inWord.CreatedAt;
                    if (wordDelTime == default || wordDelTime >= inWordTime.AddSeconds(-1))
                    {
                        continue;
                    }
                    deletedWordTimes.Remove(lowerWordKey);
                    await _dbService.RemoveTombstoneAsync("word", lowerWordKey);
                }

                if (currentWordMap.TryGetValue(text, out var localWord))
                {
                    bool shouldUpdate = false;
                    int incomingState = inWord.State == 4 ? (int)WordLearningState.Mastered : inWord.State;

                    if (localWord.StateUpdatedAt == default) localWord.StateUpdatedAt = localWord.LastReviewDate ?? localWord.CreatedAt;
                    if (localWord.MetaUpdatedAt == default) localWord.MetaUpdatedAt = localWord.CreatedAt;

                    // 维度 1：学习与算法状态合并
                    // 防御铁律：本地已有学习记录（Reps > 0 或不是新词），绝对禁止被对端尚未学习的初始新词状态（Reps == 0 && State == New）覆盖！
                    bool localHasProgress = localWord.Reps > 0 || localWord.State != (int)WordLearningState.New;
                    bool inHasProgress = inWord.Reps > 0 || incomingState != (int)WordLearningState.New;

                    if (incomingState == (int)WordLearningState.Mastered && localWord.State != (int)WordLearningState.Mastered)
                    {
                        localWord.State = (int)WordLearningState.Mastered;
                        localWord.NextReviewDate = null;
                        if (inWord.Reps > localWord.Reps)
                        {
                            localWord.Stability = inWord.Stability;
                            localWord.Difficulty = inWord.Difficulty;
                            localWord.Reps = inWord.Reps;
                            localWord.Lapses = inWord.Lapses;
                            localWord.LastReviewDate = inWord.LastReviewDate;
                        }
                        localWord.StateUpdatedAt = inWord.StateUpdatedAt > localWord.StateUpdatedAt ? inWord.StateUpdatedAt : localWord.StateUpdatedAt;
                        shouldUpdate = true;
                    }
                    else if (inHasProgress && !localHasProgress)
                    {
                        // 本地尚未学习，对端已有进度 -> 全量采纳对端学习进度
                        localWord.State = incomingState;
                        localWord.Stability = inWord.Stability;
                        localWord.Difficulty = inWord.Difficulty;
                        localWord.Reps = inWord.Reps;
                        localWord.Lapses = inWord.Lapses;
                        localWord.LastReviewDate = inWord.LastReviewDate;
                        localWord.NextReviewDate = inWord.NextReviewDate;
                        localWord.StateUpdatedAt = inWord.StateUpdatedAt;
                        shouldUpdate = true;
                    }
                    else if (inHasProgress && localHasProgress)
                    {
                        // 双端均有复习进度：采纳更高复习次数或更新时间戳
                        if (inWord.StateUpdatedAt > localWord.StateUpdatedAt.AddSeconds(1) || inWord.Reps > localWord.Reps)
                        {
                            localWord.State = incomingState;
                            localWord.Stability = inWord.Stability;
                            localWord.Difficulty = inWord.Difficulty;
                            localWord.Reps = Math.Max(localWord.Reps, inWord.Reps);
                            localWord.Lapses = Math.Max(localWord.Lapses, inWord.Lapses);
                            localWord.LastReviewDate = (inWord.LastReviewDate.HasValue && localWord.LastReviewDate.HasValue)
                                ? (inWord.LastReviewDate > localWord.LastReviewDate ? inWord.LastReviewDate : localWord.LastReviewDate)
                                : (inWord.LastReviewDate ?? localWord.LastReviewDate);
                            localWord.NextReviewDate = inWord.NextReviewDate ?? localWord.NextReviewDate;
                            localWord.StateUpdatedAt = inWord.StateUpdatedAt;
                            shouldUpdate = true;
                        }
                    }

                    // 维度 2：词单归属与元数据合并，由 MetaUpdatedAt 独立裁决
                    // 防御铁律：远端若没有词单（WordListName 为空），且本地有词单，绝不轻易清空本地词单，除非远端经历明确移出词单操作
                    bool inHasList = !string.IsNullOrWhiteSpace(inWord.WordListName);
                    bool localHasList = localWord.IsInList && (!string.IsNullOrWhiteSpace(localWord.WordListName) || localWord.WordListId.HasValue);

                    if (inHasList)
                    {
                        if (listMapByName.TryGetValue(inWord.WordListName!.Trim(), out var mappedList))
                        {
                            if (localWord.WordListId != mappedList.Id || localWord.WordListName != mappedList.Name || !localWord.IsInList)
                            {
                                localWord.WordListId = mappedList.Id;
                                localWord.WordListName = mappedList.Name;
                                localWord.IsInList = true;
                                localWord.MetaUpdatedAt = inWord.MetaUpdatedAt > localWord.MetaUpdatedAt ? inWord.MetaUpdatedAt : localWord.MetaUpdatedAt;
                                shouldUpdate = true;
                            }
                        }
                    }
                    else if (localHasList)
                    {
                        bool isExplicitUnlist = inWord.MetaUpdatedAt > inWord.CreatedAt.AddSeconds(2) &&
                                                inWord.MetaUpdatedAt > localWord.MetaUpdatedAt.AddSeconds(1);
                        if (isExplicitUnlist)
                        {
                            localWord.WordListId = null;
                            localWord.WordListName = null;
                            localWord.IsInList = false;
                            localWord.MetaUpdatedAt = inWord.MetaUpdatedAt;
                            shouldUpdate = true;
                        }
                    }

                    if (shouldUpdate)
                    {
                        await _dbService.UpdateWordAsync(localWord);
                        updatedWords++;
                    }
                }
                else
                {
                    int? targetListId = null;
                    string? targetListName = null;
                    if (!string.IsNullOrEmpty(inWord.WordListName) &&
                        listMapByName.TryGetValue(inWord.WordListName.Trim(), out var mappedList))
                    {
                        targetListId = mappedList.Id;
                        targetListName = mappedList.Name;
                    }

                    inWord.State = inWord.State == 4 ? (int)WordLearningState.Mastered : inWord.State;
                    if (inWord.State == (int)WordLearningState.Mastered)
                    {
                        inWord.NextReviewDate = null;
                    }
                    inWord.WordListId = targetListId;
                    inWord.WordListName = targetListName;
                    inWord.IsInList = targetListId.HasValue;
                    inWord.Id = 0; // 自增主键安全重置
                    await _dbService.AddWordAsync(inWord);
                    currentWordMap[text] = inWord;
                    updatedWords++;
                }
            }

            // 3. 增量追加或同日幂等合并打卡复习日志
            var existingLogs = await _dbService.GetAllReviewLogsAsync();
            var existingLogsByKey = new Dictionary<string, ReviewLog>(StringComparer.OrdinalIgnoreCase);
            var existingLogsByWordDay = new Dictionary<string, ReviewLog>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in existingLogs)
            {
                DateTime localDate = l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate;
                DateTime logicalDay = FsrsEngine.GetLogicalDate(localDate);
                string k = $"{l.WordId}_{localDate:yyyyMMddHHmmss}";
                string dayK = $"{l.WordId}_{logicalDay:yyyyMMdd}";
                existingLogsByKey[k] = l;
                if (!existingLogsByWordDay.TryGetValue(dayK, out var prevDayLog) || localDate >= prevDayLog.ReviewDate)
                {
                    existingLogsByWordDay[dayK] = l;
                }
            }

            var refreshedWords = await _dbService.GetAllWordsAsync();
            var wordIdMapByText = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in refreshedWords)
            {
                string clean = w.Text.Trim();
                if (!string.IsNullOrEmpty(clean) && !wordIdMapByText.ContainsKey(clean))
                {
                    wordIdMapByText[clean] = w.Id;
                }
            }

            var inWordTextById = new Dictionary<int, string>();
            foreach (var w in incoming.Words)
            {
                string clean = w.Text.Trim();
                if (!string.IsNullOrEmpty(clean) && !inWordTextById.ContainsKey(w.Id))
                {
                    inWordTextById[w.Id] = clean;
                }
            }

            foreach (var inLog in incoming.ReviewLogs)
            {
                // 优先从 inLog 自带的 WordText 获取，降级从 inWordTextById 获取，杜绝增量过滤时因缺词丢弃日志
                string? wordText = !string.IsNullOrWhiteSpace(inLog.WordText)
                    ? inLog.WordText.Trim()
                    : (inWordTextById.TryGetValue(inLog.WordId, out var wt) ? wt : null);

                if (string.IsNullOrEmpty(wordText)) continue;

                if (!wordIdMapByText.TryGetValue(wordText, out var localWordId)) continue;

                string cleanWordKey = wordText.ToLowerInvariant();
                if (deletedWordTimes.ContainsKey(cleanWordKey)) continue;

                DateTime localReviewDate = inLog.ReviewDate.Kind == DateTimeKind.Utc ? inLog.ReviewDate.ToLocalTime() : inLog.ReviewDate;
                inLog.ReviewDate = localReviewDate;
                DateTime logicalReviewDay = FsrsEngine.GetLogicalDate(localReviewDate);
                string timeStr = localReviewDate.ToString("yyyyMMddHHmmss");
                string dayStr = logicalReviewDay.ToString("yyyyMMdd");

                string tombKey = $"{cleanWordKey}_{timeStr}";
                if (deletedLogKeys.Contains(tombKey)) continue;

                string key = $"{localWordId}_{timeStr}";
                string dayKey = $"{localWordId}_{dayStr}";

                if (existingLogsByKey.TryGetValue(key, out var existingLog))
                {
                    if (existingLog.Rating != inLog.Rating ||
                        (inLog.StabilityAfter > 0 && Math.Abs(existingLog.StabilityAfter - inLog.StabilityAfter) > 0.0001) ||
                        (inLog.DifficultyAfter > 0 && Math.Abs(existingLog.DifficultyAfter - inLog.DifficultyAfter) > 0.0001))
                    {
                        existingLog.Rating = inLog.Rating;
                        if (inLog.StabilityBefore > 0) existingLog.StabilityBefore = inLog.StabilityBefore;
                        if (inLog.StabilityAfter > 0) existingLog.StabilityAfter = inLog.StabilityAfter;
                        if (inLog.DifficultyBefore > 0) existingLog.DifficultyBefore = inLog.DifficultyBefore;
                        if (inLog.DifficultyAfter > 0) existingLog.DifficultyAfter = inLog.DifficultyAfter;
                        if (inLog.State > 0) existingLog.State = inLog.State;
                        if (inLog.ElapsedDays > 0) existingLog.ElapsedDays = inLog.ElapsedDays;
                        if (inLog.ScheduledDays > 0) existingLog.ScheduledDays = inLog.ScheduledDays;
                        await _dbService.UpdateReviewLogAsync(existingLog);
                    }
                }
                else if (existingLogsByWordDay.TryGetValue(dayKey, out var sameDayLog))
                {
                    // 同一逻辑日打卡：以较新记录为准更新参数，严禁伪造删除墓碑
                    DateTime existingLocalDate = sameDayLog.ReviewDate.Kind == DateTimeKind.Utc
                        ? sameDayLog.ReviewDate.ToLocalTime()
                        : sameDayLog.ReviewDate;

                    if (localReviewDate >= existingLocalDate)
                    {
                        sameDayLog.ReviewDate = localReviewDate;
                        sameDayLog.Rating = inLog.Rating;
                        if (inLog.StabilityBefore > 0) sameDayLog.StabilityBefore = inLog.StabilityBefore;
                        if (inLog.StabilityAfter > 0) sameDayLog.StabilityAfter = inLog.StabilityAfter;
                        if (inLog.DifficultyBefore > 0) sameDayLog.DifficultyBefore = inLog.DifficultyBefore;
                        if (inLog.DifficultyAfter > 0) sameDayLog.DifficultyAfter = inLog.DifficultyAfter;
                        sameDayLog.State = inLog.State;
                        sameDayLog.ElapsedDays = inLog.ElapsedDays;
                        sameDayLog.ScheduledDays = inLog.ScheduledDays;
                        await _dbService.UpdateReviewLogAsync(sameDayLog);
                        existingLogsByKey[key] = sameDayLog;
                    }
                }
                else
                {
                    inLog.WordId = localWordId;
                    inLog.Id = 0;
                    await _dbService.AddReviewLogAsync(inLog);
                    existingLogsByKey[key] = inLog;
                    existingLogsByWordDay[dayKey] = inLog;
                    insertedLogs++;
                }
            }

            // 4. 复习日志与 FSRS 状态重演自愈
            var allLogs = await _dbService.GetAllReviewLogsAsync();
            var logsByWordId = allLogs
                .GroupBy(l => l.WordId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.ReviewDate).ThenBy(l => l.Id).ToList());
            var wordsToCheck = await _dbService.GetAllWordsAsync();

            foreach (var w in wordsToCheck)
            {
                // 关键自愈门禁：只要该词在数据库中拥有打卡记录，就必须纳入检验与自愈，绝不能因为 w.State == New 而跳过！
                if (logsByWordId.TryGetValue(w.Id, out var wLogs) && wLogs.Count > 0)
                {
                    bool needsReplay = w.Reps != wLogs.Count || w.State == (int)WordLearningState.New || w.Lapses != wLogs.Count(l => l.Rating == 1);
                    if (!needsReplay)
                    {
                        for (int i = 1; i < wLogs.Count; i++)
                        {
                            if (Math.Abs(wLogs[i].StabilityBefore - wLogs[i - 1].StabilityAfter) > 0.001)
                            {
                                needsReplay = true;
                                break;
                            }
                        }
                        var lastL = wLogs[wLogs.Count - 1];
                        if (lastL.StabilityAfter > 0 && Math.Abs(w.Stability - lastL.StabilityAfter) > 0.001)
                        {
                            needsReplay = true;
                        }
                    }

                    if (needsReplay)
                    {
                        var simWord = new Word
                        {
                            Id = w.Id,
                            Text = w.Text,
                            CreatedAt = w.CreatedAt,
                            State = (int)WordLearningState.New,
                            Stability = 0.0,
                            Difficulty = 0.0,
                            Reps = 0,
                            Lapses = 0,
                            LastReviewDate = null
                        };

                        foreach (var rLog in wLogs)
                        {
                            int rPreState = simWord.State;
                            double rPreS = simWord.Stability;
                            double rPreD = simWord.Difficulty;
                            int rElapsed = simWord.Reps == 0
                                ? 0
                                : (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(simWord.LastReviewDate ?? w.CreatedAt, rLog.ReviewDate));
                            var (rNewS, rNewD, rNext) = FsrsEngine.Review(simWord, rLog.Rating, rLog.ReviewDate);
                            int rSched = Math.Max(1, (int)Math.Round((rNext.Date - FsrsEngine.GetLogicalDate(rLog.ReviewDate)).TotalDays));

                            if (Math.Abs(rLog.StabilityBefore - rPreS) > 0.001 ||
                                Math.Abs(rLog.StabilityAfter - rNewS) > 0.001 ||
                                Math.Abs(rLog.DifficultyBefore - rPreD) > 0.001 ||
                                Math.Abs(rLog.DifficultyAfter - rNewD) > 0.001)
                            {
                                rLog.StabilityBefore = rPreS;
                                rLog.DifficultyBefore = rPreD;
                                rLog.StabilityAfter = rNewS;
                                rLog.DifficultyAfter = rNewD;
                                rLog.State = rPreState;
                                rLog.ElapsedDays = rElapsed;
                                rLog.ScheduledDays = rSched;
                                await _dbService.UpdateReviewLogAsync(rLog);
                            }

                            simWord.Stability = rNewS;
                            simWord.Difficulty = rNewD;
                            simWord.LastReviewDate = rLog.ReviewDate;
                            simWord.NextReviewDate = rNext;
                            simWord.Reps += 1;
                            if (rLog.Rating == 1) simWord.Lapses += 1;
                            simWord.State = (rLog.Rating == 1 || simWord.Reps < 2)
                                ? (int)WordLearningState.Learning
                                : (int)WordLearningState.Review;
                        }

                        w.Reps = simWord.Reps;
                        w.Lapses = simWord.Lapses;
                        w.Stability = simWord.Stability;
                        w.Difficulty = simWord.Difficulty;
                        w.LastReviewDate = simWord.LastReviewDate;
                        w.StateUpdatedAt = simWord.LastReviewDate ?? DateTime.Now;
                        if (w.State == (int)WordLearningState.Mastered)
                        {
                            w.NextReviewDate = null;
                        }
                        else
                        {
                            w.State = simWord.State;
                            w.NextReviewDate = simWord.NextReviewDate;
                        }
                        await _dbService.UpdateWordAsync(w);
                    }
                }
            }

            await _dbService.CleanupExpiredTombstonesAsync(90);
            var latestTombstones = await _dbService.GetAllTombstonesAsync();

            return new SyncResult
            {
                Success = true,
                Message = "同步成功完成",
                SyncedLists = syncedLists,
                SyncedWords = updatedWords,
                SyncedLogs = insertedLogs,
                Tombstones = latestTombstones
            };
        }

        public void Stop()
        {
            try
            {
                _serverCts?.Cancel();
                _tcpListener?.Stop();

                foreach (var kvp in _sseClients)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _sseClients.Clear();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "HttpSyncServer.Stop");
            }
            finally
            {
                _tcpListener = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _serverCts?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            if (_listenTask != null)
            {
                try { await _listenTask.ConfigureAwait(false); } catch { }
            }
            _serverCts?.Dispose();
        }
    }
}
#endif

