using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{

    public class NetworkEndpointInfo
    {
        public string IpAddress { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string InterfaceTypeDescription { get; set; } = string.Empty;
        public bool IsEthernet { get; set; }
        public string DisplayUrl { get; set; } = string.Empty;
    }

    public class LanSyncService
    {
        private static LanSyncService? _instance;
        public static LanSyncService Instance => _instance ??= new LanSyncService();

        public const int DefaultPort = 52080;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private readonly DatabaseService _dbService;
        private readonly ConcurrentDictionary<string, (int Failures, DateTime LockedUntilUtc)> _pinRateLimiter = new();

        private static readonly JsonSerializerOptions _incomingJsonOptions = CreateIncomingJsonOptions();
        private static readonly JsonSerializerOptions _outgoingJsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static JsonSerializerOptions CreateIncomingJsonOptions()
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            opts.Converters.Add(new FlexibleBoolConverter());
            return opts;
        }

        public bool IsRunning { get; private set; }
        public string CurrentPin { get; private set; } = GeneratePin();
        public int Port { get; private set; } = DefaultPort;

        public event Action<bool>? StatusChanged;
        public event Action<string>? LogReceived;

        public LanSyncService(DatabaseService? dbService = null, int port = DefaultPort)
        {
            _dbService = dbService ?? new DatabaseService();
            Port = port;
        }

        public static string GeneratePin()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }

        public void RefreshPin()
        {
            CurrentPin = GeneratePin();
            _pinRateLimiter.Clear();
            LogReceived?.Invoke($"安全配对 PIN 码已刷新为: {CurrentPin}");
        }

        private bool VerifyPinConstantTime(string clientPin)
        {
            if (string.IsNullOrWhiteSpace(clientPin)) return false;
            byte[] a = Encoding.UTF8.GetBytes(clientPin.Trim());
            byte[] b = Encoding.UTF8.GetBytes(CurrentPin);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>
        /// 获取本机物理网络接口列表（严格优先选择物理以太网网线、物理无线WiFi，排除虚拟机与TUN等虚拟网卡）
        /// </summary>
        public static List<NetworkEndpointInfo> GetNetworkEndpoints(int port = DefaultPort)
        {
            var results = new List<NetworkEndpointInfo>();
            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus != OperationalStatus.Up ||
                        netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    string name = netInterface.Name.ToLowerInvariant();
                    string desc = netInterface.Description.ToLowerInvariant();

                    // 严格过滤常见的虚拟网卡与代理隧道网卡
                    if (name.Contains("vethernet") || desc.Contains("virtual") || desc.Contains("hyper-v") ||
                        name.Contains("wsl") || name.Contains("tun") || name.Contains("tap") ||
                        name.Contains("zodaccess") || desc.Contains("vpn") || desc.Contains("pseudo"))
                    {
                        continue;
                    }

                    bool isEthernet = netInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
                    bool isWireless = netInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                    var ipProps = netInterface.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = addr.Address.ToString();
                            if (ipStr.StartsWith("127.") || ipStr.StartsWith("169.254."))
                            {
                                continue;
                            }

                            string typeDesc = isEthernet ? "以太网(网线)" : (isWireless ? "WiFi" : "局域网");
                            results.Add(new NetworkEndpointInfo
                            {
                                IpAddress = ipStr,
                                InterfaceName = netInterface.Name,
                                InterfaceTypeDescription = typeDesc,
                                IsEthernet = isEthernet,
                                DisplayUrl = $"http://{ipStr}:{port}"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "LanSyncService.GetNetworkEndpoints");
            }

            // 排序：物理网线以太网排在最首位（如 192.168.x.x），其次是 WiFi
            return results.OrderByDescending(e => e.IsEthernet)
                          .ThenBy(e => e.IpAddress.StartsWith("192.168.") ? 0 : 1)
                          .ToList();
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            try
            {
                await _dbService.InitializeAsync();
                _cts = new CancellationTokenSource();

                // 使用 TcpListener 监听 IPAddress.Any:Port
                // 1. 无需管理员特权，普通用户权限即可秒级绑定
                // 2. 自动接受所有物理网卡（以太网有线、WiFi）进来的所有局域网流量
                try
                {
                    _tcpListener = new TcpListener(IPAddress.Any, Port);
                    _tcpListener.Start();
                }
                catch (SocketException) when (Port != 0)
                {
                    _tcpListener = new TcpListener(IPAddress.Any, 0);
                    _tcpListener.Start();
                }
                Port = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;

                IsRunning = true;
                StatusChanged?.Invoke(true);

                var endpoints = GetNetworkEndpoints();
                string primaryDesc = endpoints.Count > 0
                    ? $"{endpoints[0].DisplayUrl} ({endpoints[0].InterfaceTypeDescription})"
                    : $"http://127.0.0.1:{Port}";

                LogReceived?.Invoke($"局域网同步服务已启动！监听端口: {Port}");
                LogReceived?.Invoke($"本机主要地址: {primaryDesc}");
                LogReceived?.Invoke("电脑已连网线，手机连同一路由器 WiFi 即可直接通信。");

                _ = AcceptLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                StatusChanged?.Invoke(false);
                LogReceived?.Invoke($"启动局域网同步服务失败: {ex.Message}");
                CrashLogger.LogException(ex, "LanSyncService.StartAsync");
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;
            try
            {
                _cts?.Cancel();
                _tcpListener?.Stop();
            }
            catch { }
            finally
            {
                _tcpListener = null;
                _cts?.Dispose();
                _cts = null;
                _pinRateLimiter.Clear();
                IsRunning = false;
                StatusChanged?.Invoke(false);
                LogReceived?.Invoke("局域网同步服务已停止。");
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleTcpClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        LogReceived?.Invoke($"连接处理异常: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
                var reqToken = timeoutCts.Token;

                try
                {
                    stream.ReadTimeout = 15000;
                    stream.WriteTimeout = 15000;

                    // 异步读取 HTTP 请求报文头（避免同步 ReadByte 阻塞线程池）
                    const int MaxHeaderBytes = 65536;
                    byte[] readBuf = new byte[4096];
                    using var requestBuffer = new MemoryStream();
                    int headerEndIndex = -1;

                    while (headerEndIndex < 0)
                    {
                        int n = await stream.ReadAsync(readBuf, 0, readBuf.Length, reqToken);
                        if (n <= 0) break;

                        requestBuffer.Write(readBuf, 0, n);
                        headerEndIndex = FindHeaderEnd(requestBuffer.GetBuffer(), (int)requestBuffer.Length);

                        if (headerEndIndex < 0 && requestBuffer.Length > MaxHeaderBytes)
                        {
                            await SendHttpResponseAsync(stream, 400, "Header Too Large");
                            return;
                        }
                    }

                    if (headerEndIndex < 0) return;

                    byte[] rawBuf = requestBuffer.GetBuffer();
                    int totalBuffered = (int)requestBuffer.Length;
                    int bodyStartOffset = headerEndIndex + 4;

                    string headerString = Encoding.UTF8.GetString(rawBuf, 0, headerEndIndex);
                    var headerLines = headerString.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (headerLines.Length == 0) return;

                    var requestLineParts = headerLines[0].Split(' ');
                    if (requestLineParts.Length < 2) return;

                    string method = requestLineParts[0].ToUpperInvariant();
                    string path = requestLineParts[1];

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

                    // CORS 预检请求
                    if (method == "OPTIONS")
                    {
                        await SendHttpResponseAsync(stream, 200, "OK");
                        return;
                    }

                    // 1. GET /api/ping
                    if (path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase) && method == "GET")
                    {
                        var pingObj = new
                        {
                            status = "ok",
                            device = Environment.MachineName,
                            version = typeof(LanSyncService).Assembly.GetName().Version?.ToString(3) ?? "1.4.6",
                            server_time = DateTime.Now
                        };
                        await SendHttpJsonAsync(stream, 200, pingObj, reqToken);
                        return;
                    }

                    // 2. 检查配对 PIN 码（常数时间比对 + IP 级防暴力破解限流）
                    string remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";
                    if (_pinRateLimiter.TryGetValue(remoteIp, out var limitInfo) && DateTime.UtcNow < limitInfo.LockedUntilUtc)
                    {
                        await SendHttpJsonAsync(stream, 429, new { error = "Too Many Requests: PIN temporarily locked due to repeated failures" }, reqToken);
                        return;
                    }

                    headers.TryGetValue("X-Sync-Pin", out var clientPin);
                    clientPin ??= "";

                    if (!VerifyPinConstantTime(clientPin))
                    {
                        _pinRateLimiter.AddOrUpdate(
                            remoteIp,
                            _ => (1, DateTime.MinValue),
                            (_, prev) =>
                            {
                                int nextFailures = prev.Failures + 1;
                                DateTime lockedUntil = nextFailures >= 5 ? DateTime.UtcNow.AddSeconds(30) : DateTime.MinValue;
                                return (nextFailures, lockedUntil);
                            });

                        LogReceived?.Invoke($"[拒绝] 客户端 ({client.Client.RemoteEndPoint}) PIN 码验证未通过。");
                        await SendHttpJsonAsync(stream, 401, new { error = "Unauthorized: Invalid X-Sync-Pin" }, reqToken);
                        return;
                    }

                    _pinRateLimiter.TryRemove(remoteIp, out _);

                    // 3. POST /api/sync/pull (拉取全部)
                    if (path.Equals("/api/sync/pull", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        LogReceived?.Invoke($"[拉取数据] 手机端 ({client.Client.RemoteEndPoint}) 正在拉取词库与进度...");
                        var payload = await BuildCurrentPayloadAsync();
                        await SendHttpJsonAsync(stream, 200, payload, reqToken);
                        LogReceived?.Invoke($"[发送完成] 已传输 {payload.Words.Count} 个单词与 {payload.ReviewLogs.Count} 条打卡记录！");
                        return;
                    }

                    // 4. POST /api/sync/push (手机推送数据合并)
                    if (path.Equals("/api/sync/push", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        LogReceived?.Invoke($"[双向合并] 收到手机端 ({client.Client.RemoteEndPoint}) 数据包，正在合并...");

                        const int MaxPayloadBytes = 50 * 1024 * 1024; // 50 MB 上限防 OOM
                        int contentLength = 0;
                        if (headers.TryGetValue("Content-Length", out var clStr))
                        {
                            int.TryParse(clStr, out contentLength);
                        }

                        if (contentLength <= 0 || contentLength > MaxPayloadBytes)
                        {
                            await SendHttpJsonAsync(stream, 413, new { error = "Payload Too Large or Invalid Content-Length" }, reqToken);
                            return;
                        }

                        byte[] bodyBytes = new byte[contentLength];
                        int alreadyBufferedBody = Math.Min(contentLength, Math.Max(0, totalBuffered - bodyStartOffset));
                        if (alreadyBufferedBody > 0)
                        {
                            Buffer.BlockCopy(rawBuf, bodyStartOffset, bodyBytes, 0, alreadyBufferedBody);
                        }

                        int totalRead = alreadyBufferedBody;
                        while (totalRead < contentLength)
                        {
                            int read = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead, reqToken);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        if (totalRead < contentLength)
                        {
                            await SendHttpJsonAsync(stream, 400, new { error = "Incomplete request body" }, reqToken);
                            return;
                        }

                        string bodyJson = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
                        var incomingPayload = JsonSerializer.Deserialize<SyncPayload>(bodyJson, _incomingJsonOptions);

                        if (incomingPayload == null)
                        {
                            await SendHttpJsonAsync(stream, 400, new { error = "Invalid JSON" }, reqToken);
                            return;
                        }

                        var result = await MergeIncomingPayloadAsync(incomingPayload);
                        await SendHttpJsonAsync(stream, 200, result, reqToken);
                        LogReceived?.Invoke($"[合并成功] 更新单词: {result.SyncedWords}，新日志: {result.SyncedLogs}！");
                        DatabaseService.NotifyDataChanged();
                        return;
                    }

                    await SendHttpResponseAsync(stream, 404, "Not Found", reqToken);
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"请求处理异常: {ex.Message}");
                    CrashLogger.LogException(ex, "LanSyncService.HandleTcpClientAsync");
                    try
                    {
                        await SendHttpJsonAsync(stream, 500, new { error = ex.Message }, reqToken);
                    }
                    catch { }
                }
                finally
                {
                    try
                    {
                        if (client.Connected)
                        {
                            client.Client.Shutdown(SocketShutdown.Both);
                        }
                    }
                    catch { }
                }
            }
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
                    buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        private static string GetStatusText(int statusCode) => statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            413 => "Payload Too Large",
            429 => "Too Many Requests",
            500 => "Internal Server Error",
            _ => "OK"
        };

        private static async Task SendHttpResponseAsync(Stream stream, int statusCode, string body, CancellationToken token = default)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            string headers = $"HTTP/1.1 {statusCode} {GetStatusText(statusCode)}\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                             "Access-Control-Allow-Headers: Content-Type, X-Sync-Pin\r\n" +
                             "Content-Type: text/plain; charset=utf-8\r\n" +
                             $"Content-Length: {bytes.Length}\r\n" +
                             "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), token);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), token);
            await stream.FlushAsync(token);
        }

        private static async Task SendHttpJsonAsync(Stream stream, int statusCode, object data, CancellationToken token = default)
        {
            string json = JsonSerializer.Serialize(data, _outgoingJsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string headers = $"HTTP/1.1 {statusCode} {GetStatusText(statusCode)}\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                             "Access-Control-Allow-Headers: Content-Type, X-Sync-Pin\r\n" +
                             "Content-Type: application/json; charset=utf-8\r\n" +
                             $"Content-Length: {bytes.Length}\r\n" +
                             "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), token);
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length), token);
            await stream.FlushAsync(token);
        }

        private async Task<SyncPayload> BuildCurrentPayloadAsync()
        {
            var lists = await _dbService.GetAllWordListsAsync();
            var allWords = await _dbService.GetAllWordsAsync();
            var logs = await _dbService.GetAllReviewLogsAsync();
            var tombstones = await _dbService.GetAllTombstonesAsync();

            var wordMapById = allWords.ToDictionary(w => w.Id, w => w.Text);

            foreach (var w in allWords)
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

            return new SyncPayload
            {
                WordLists = lists,
                Words = allWords,
                ReviewLogs = logs,
                Tombstones = tombstones,
                Timestamp = DateTime.Now,
                DeviceName = Environment.MachineName
            };
        }

        private async Task<SyncResult> MergeIncomingPayloadAsync(SyncPayload incoming)
        {
            // 自动快照：在任何合并执行前对数据库进行原子备份，保留近 10 份快照
            try
            {
                await _dbService.CreateAutomaticBackupAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "LanSyncService.MergeIncomingPayloadAsync.CreateAutomaticBackupAsync");
            }

            // 抑制合并过程中的高频逐项 DataChanged 事件风暴，统一在同步结束时刷新一次
            using var _ = DatabaseService.SuppressNotifications();

            int updatedWords = 0;
            int insertedLogs = 0;
            int syncedLists = 0;

            // 0. 双向应用删除墓碑 (Tombstones)：删除本地已在对端被删除的词单、单词与撤销的记录
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

                // 构建 O(1) 哈希索引，消除原先在墓碑循环内的 O(T * W)、O(T * L)、O(T * R) 线性查找
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

            // 重新获取删除后的最新单词列表与词单列表
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

                // 健全兜底传入端的时间戳
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

                    // 【维度 1：学习与算法状态合并】
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

                    // 【维度 2：词单归属与元数据合并，由 MetaUpdatedAt 独立裁决】
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

            // 3. 增量追加或同日幂等合并打卡复习日志（按逻辑日 WordId + yyyyMMdd 保证每日至多 1 条日志）
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

            // 4. 复习日志与 FSRS 状态重演自愈（解决双端跨日离线各自打卡后的状态与日志链分裂）：
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
    }
}
