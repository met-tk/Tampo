using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public class FlexibleBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True) return true;
            if (reader.TokenType == JsonTokenType.False) return false;
            if (reader.TokenType == JsonTokenType.Number) return reader.GetInt32() != 0;
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (bool.TryParse(str, out var b)) return b;
                if (str == "1") return true;
                if (str == "0") return false;
            }
            return false;
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }
    }
    public class SyncPayload
    {
        public List<WordList> WordLists { get; set; } = new();
        public List<Word> Words { get; set; } = new();
        public List<ReviewLog> ReviewLogs { get; set; } = new();
        public List<SyncTombstone> Tombstones { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string DeviceName { get; set; } = Environment.MachineName;
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SyncedLists { get; set; }
        public int SyncedWords { get; set; }
        public int SyncedLogs { get; set; }
        public List<SyncTombstone> Tombstones { get; set; } = new();
    }

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

        public bool IsRunning { get; private set; }
        public string CurrentPin { get; private set; } = GeneratePin();
        public int Port { get; private set; } = DefaultPort;

        public event Action<bool>? StatusChanged;
        public event Action<string>? LogReceived;

        private LanSyncService()
        {
            _dbService = new DatabaseService();
        }

        public static string GeneratePin()
        {
            return System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        }

        public void RefreshPin()
        {
            CurrentPin = GeneratePin();
            LogReceived?.Invoke($"安全配对 PIN 码已刷新为: {CurrentPin}");
        }

        /// <summary>
        /// 获取本机物理网络接口列表（严格优先选择物理以太网网线、物理无线WiFi，排除虚拟机与TUN等虚拟网卡）
        /// </summary>
        public List<NetworkEndpointInfo> GetNetworkEndpoints()
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
                                DisplayUrl = $"http://{ipStr}:{Port}"
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

        public List<string> GetLocalIpAddresses()
        {
            var endpoints = GetNetworkEndpoints();
            if (endpoints.Count > 0)
            {
                return endpoints.Select(e => e.IpAddress).ToList();
            }
            return new List<string> { "127.0.0.1" };
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
                _cts = null;
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
            {
                try
                {
                    stream.ReadTimeout = 15000;
                    stream.WriteTimeout = 15000;

                    // 读取 HTTP 请求报文头
                    var headerBytes = new List<byte>();
                    int b;

                    while ((b = stream.ReadByte()) != -1)
                    {
                        headerBytes.Add((byte)b);
                        if (b == '\r' || b == '\n')
                        {
                            if (headerBytes.Count >= 4)
                            {
                                int len = headerBytes.Count;
                                if (headerBytes[len - 4] == '\r' && headerBytes[len - 3] == '\n' &&
                                    headerBytes[len - 2] == '\r' && headerBytes[len - 1] == '\n')
                                {
                                    break;
                                }
                            }
                        }
                        if (headerBytes.Count > 65536) // 防恶意超长 Header
                        {
                            await SendHttpResponseAsync(stream, 400, "Header Too Large");
                            return;
                        }
                    }

                    if (headerBytes.Count == 0) return;

                    string headerString = Encoding.UTF8.GetString(headerBytes.ToArray());
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
                            version = typeof(LanSyncService).Assembly.GetName().Version?.ToString(3) ?? "1.1.7",
                            server_time = DateTime.Now
                        };
                        await SendHttpJsonAsync(stream, 200, pingObj);
                        return;
                    }

                    // 2. 检查配对 PIN 码
                    headers.TryGetValue("X-Sync-Pin", out var clientPin);
                    clientPin ??= "";

                    if (string.IsNullOrWhiteSpace(clientPin) || clientPin != CurrentPin)
                    {
                        LogReceived?.Invoke($"[拒绝] 客户端 ({client.Client.RemoteEndPoint}) PIN 码验证未通过。");
                        await SendHttpJsonAsync(stream, 401, new { error = "Unauthorized: Invalid X-Sync-Pin" });
                        return;
                    }

                    // 3. POST /api/sync/pull (拉取全部)
                    if (path.Equals("/api/sync/pull", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        LogReceived?.Invoke($"[拉取数据] 手机端 ({client.Client.RemoteEndPoint}) 正在拉取词库与进度...");
                        var payload = await BuildCurrentPayloadAsync();
                        await SendHttpJsonAsync(stream, 200, payload);
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
                            await SendHttpJsonAsync(stream, 413, new { error = "Payload Too Large or Invalid Content-Length" });
                            return;
                        }

                        byte[] bodyBytes = new byte[contentLength];
                        int totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            int read = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead, token);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        string bodyJson = Encoding.UTF8.GetString(bodyBytes, 0, totalRead);
                        var jsonOptions = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            NumberHandling = JsonNumberHandling.AllowReadingFromString
                        };
                        jsonOptions.Converters.Add(new FlexibleBoolConverter());

                        var incomingPayload = JsonSerializer.Deserialize<SyncPayload>(bodyJson, jsonOptions);

                        if (incomingPayload == null)
                        {
                            await SendHttpJsonAsync(stream, 400, new { error = "Invalid JSON" });
                            return;
                        }

                        var result = await MergeIncomingPayloadAsync(incomingPayload);
                        await SendHttpJsonAsync(stream, 200, result);
                        LogReceived?.Invoke($"[合并成功] 更新单词: {result.SyncedWords}，新日志: {result.SyncedLogs}！");
                        DatabaseService.NotifyDataChanged();
                        return;
                    }

                    await SendHttpResponseAsync(stream, 404, "Not Found");
                }
                catch (Exception ex)
                {
                    LogReceived?.Invoke($"请求处理异常: {ex.Message}");
                    CrashLogger.LogException(ex, "LanSyncService.HandleTcpClientAsync");
                    try
                    {
                        await SendHttpJsonAsync(stream, 500, new { error = ex.Message });
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

        private static async Task SendHttpResponseAsync(Stream stream, int statusCode, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            string headers = $"HTTP/1.1 {statusCode} OK\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                             "Access-Control-Allow-Headers: Content-Type, X-Sync-Pin\r\n" +
                             "Content-Type: text/plain; charset=utf-8\r\n" +
                             $"Content-Length: {bytes.Length}\r\n" +
                             "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private static async Task SendHttpJsonAsync(Stream stream, int statusCode, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            string headers = $"HTTP/1.1 {statusCode} OK\r\n" +
                             "Access-Control-Allow-Origin: *\r\n" +
                             "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                             "Access-Control-Allow-Headers: Content-Type, X-Sync-Pin\r\n" +
                             "Content-Type: application/json; charset=utf-8\r\n" +
                             $"Content-Length: {bytes.Length}\r\n" +
                             "Connection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private async Task<SyncPayload> BuildCurrentPayloadAsync()
        {
            var lists = await _dbService.GetAllWordListsAsync();
            var words = await _dbService.GetAllWordsAsync();
            var logs = await _dbService.GetAllReviewLogsAsync();
            var tombstones = await _dbService.GetAllTombstonesAsync();

            return new SyncPayload
            {
                WordLists = lists,
                Words = words,
                ReviewLogs = logs,
                Tombstones = tombstones,
                Timestamp = DateTime.Now,
                DeviceName = Environment.MachineName
            };
        }

        private async Task<SyncResult> MergeIncomingPayloadAsync(SyncPayload incoming)
        {
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

            var deletedLogKeys = new HashSet<string>(localTombstones.Where(t => t.EntityType == "review_log").Select(t => t.EntityKey.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

            var currentWords = await _dbService.GetAllWordsAsync();

            if (incoming.Tombstones != null && incoming.Tombstones.Count > 0)
            {
                foreach (var inTomb in incoming.Tombstones)
                {
                    string cleanKey = inTomb.EntityKey.Trim().ToLowerInvariant();
                    DateTime tombDeletedAt = inTomb.DeletedAt.Kind == DateTimeKind.Utc ? inTomb.DeletedAt.ToLocalTime() : inTomb.DeletedAt;
                    if (inTomb.EntityType == "word")
                    {
                        var match = currentWords.FirstOrDefault(w => w.Text.Trim().Equals(cleanKey, StringComparison.OrdinalIgnoreCase));
                        DateTime matchTime = match != null ? (match.MetaUpdatedAt > match.CreatedAt ? match.MetaUpdatedAt : match.CreatedAt) : default;

                        if (match == null || tombDeletedAt == default || tombDeletedAt >= matchTime.AddSeconds(-1))
                        {
                            deletedWordTimes[cleanKey] = tombDeletedAt;
                            await _dbService.AddTombstoneAsync("word", cleanKey, tombDeletedAt);

                            if (match != null)
                            {
                                await _dbService.DeleteWordsAsync(new[] { match.Id });
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
                        var currentListsNow = await _dbService.GetAllWordListsAsync();
                        var match = currentListsNow.FirstOrDefault(l => l.Name.Trim().Equals(cleanKey, StringComparison.OrdinalIgnoreCase));
                        DateTime matchTime = match != null ? (match.UpdatedAt > match.CreatedAt ? match.UpdatedAt : match.CreatedAt) : default;

                        if (match == null || tombDeletedAt == default || tombDeletedAt >= matchTime.AddSeconds(-1))
                        {
                            deletedListTimes[cleanKey] = tombDeletedAt;
                            await _dbService.AddTombstoneAsync("word_list", cleanKey, tombDeletedAt);

                            if (match != null)
                            {
                                await _dbService.DeleteWordListAsync(match.Id);
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
                            var matchWord = currentWords.FirstOrDefault(w => w.Text.Trim().Equals(wText, StringComparison.OrdinalIgnoreCase));
                            if (matchWord != null)
                            {
                                var logs = await _dbService.GetAllReviewLogsAsync();
                                var matchLog = logs.FirstOrDefault(l =>
                                    l.WordId == matchWord.Id &&
                                    (l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate).ToString("yyyyMMddHHmmss") == timeStr);
                                if (matchLog != null)
                                {
                                    await _dbService.RevertTodayReviewAsync(matchLog.Id, isSyncRevert: true);
                                }
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

                    // 【维度 1：学习与算法状态合并，由 StateUpdatedAt 裁决】
                    // 仅当传入端的学习状态更新时采纳，绝不被任何非复习动作冲刷覆盖
                    if (inWord.StateUpdatedAt > localWord.StateUpdatedAt.AddSeconds(1))
                    {
                        localWord.State = incomingState;
                        localWord.StateUpdatedAt = inWord.StateUpdatedAt;
                        if (incomingState == (int)WordLearningState.Mastered)
                        {
                            // 【已掌握】独立于 FSRS 之外，免除排期，不将旧版伪造的 Stability=25.0 污染覆盖本地真实复习历史
                            localWord.NextReviewDate = null;
                            if (inWord.Reps > localWord.Reps)
                            {
                                localWord.Stability = inWord.Stability;
                                localWord.Difficulty = inWord.Difficulty;
                                localWord.Reps = inWord.Reps;
                                localWord.Lapses = inWord.Lapses;
                                localWord.LastReviewDate = inWord.LastReviewDate;
                            }
                        }
                        else
                        {
                            localWord.Stability = inWord.Stability;
                            localWord.Difficulty = inWord.Difficulty;
                            localWord.Reps = inWord.Reps;
                            localWord.Lapses = inWord.Lapses;
                            localWord.LastReviewDate = inWord.LastReviewDate;
                            localWord.NextReviewDate = inWord.NextReviewDate;
                        }
                        shouldUpdate = true;
                    }
                    else if (incomingState == (int)WordLearningState.Mastered &&
                             localWord.State != (int)WordLearningState.Mastered &&
                             inWord.StateUpdatedAt >= localWord.StateUpdatedAt.AddSeconds(-1))
                    {
                        // 显式已掌握属性安全保底（仅设置状态与免复习，不覆盖 FSRS 稳定性或复习时间）
                        localWord.State = (int)WordLearningState.Mastered;
                        localWord.NextReviewDate = null;
                        localWord.StateUpdatedAt = inWord.StateUpdatedAt > localWord.StateUpdatedAt ? inWord.StateUpdatedAt : localWord.StateUpdatedAt;
                        shouldUpdate = true;
                    }

                    // 【维度 2：词单归属与元数据合并，由 MetaUpdatedAt 独立裁决】
                    if (inWord.MetaUpdatedAt > localWord.MetaUpdatedAt.AddSeconds(1))
                    {
                        int? targetListId = null;
                        string? targetListName = null;
                        if (!string.IsNullOrEmpty(inWord.WordListName) &&
                            listMapByName.TryGetValue(inWord.WordListName.Trim(), out var mappedList))
                        {
                            targetListId = mappedList.Id;
                            targetListName = mappedList.Name;
                        }

                        localWord.WordListId = targetListId;
                        localWord.WordListName = targetListName;
                        localWord.IsInList = targetListId.HasValue;
                        localWord.MetaUpdatedAt = inWord.MetaUpdatedAt;
                        shouldUpdate = true;
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

            // 3. 增量追加或同日幂等合并打卡复习日志（按自然日 WordId + yyyyMMdd 保证每日至多 1 条日志）
            var existingLogs = await _dbService.GetAllReviewLogsAsync();
            var existingLogsByKey = new Dictionary<string, ReviewLog>(StringComparer.OrdinalIgnoreCase);
            var existingLogsByWordDay = new Dictionary<string, ReviewLog>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in existingLogs)
            {
                DateTime localDate = l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate;
                string k = $"{l.WordId}_{localDate:yyyyMMddHHmmss}";
                string dayK = $"{l.WordId}_{localDate:yyyyMMdd}";
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
                if (inWordTextById.TryGetValue(inLog.WordId, out var wordText) &&
                    wordIdMapByText.TryGetValue(wordText, out var localWordId))
                {
                    string cleanWordKey = wordText.Trim().ToLowerInvariant();
                    if (deletedWordTimes.ContainsKey(cleanWordKey)) continue;

                    DateTime localReviewDate = inLog.ReviewDate.Kind == DateTimeKind.Utc ? inLog.ReviewDate.ToLocalTime() : inLog.ReviewDate;
                    inLog.ReviewDate = localReviewDate;
                    string timeStr = localReviewDate.ToString("yyyyMMddHHmmss");
                    string dayStr = localReviewDate.ToString("yyyyMMdd");

                    string tombKey = $"{cleanWordKey}_{timeStr}";
                    // 若已被撤销删除，严禁复活！
                    if (deletedLogKeys.Contains(tombKey)) continue;

                    string key = $"{localWordId}_{timeStr}";
                    string dayKey = $"{localWordId}_{dayStr}";

                    if (existingLogsByKey.TryGetValue(key, out var existingLog))
                    {
                        if (existingLog.Rating != inLog.Rating ||
                            (inLog.StabilityAfter > 0 && Math.Abs(existingLog.StabilityAfter - inLog.StabilityAfter) > 0.0001) ||
                            (inLog.DifficultyAfter > 0 && Math.Abs(existingLog.DifficultyAfter - inLog.DifficultyAfter) > 0.0001))
                        {
                            // 同日改判同步：原地更新已有日志的 Rating 与 FSRS 快照，严禁重复插入导致 Reps 虚增
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
                        // 双端同日不同时刻离线打卡幂等归一：同一单词在同一自然日最多保留 1 条最新记录，旧时间戳写入墓碑防复活
                        DateTime existingLocalDate = sameDayLog.ReviewDate.Kind == DateTimeKind.Utc
                            ? sameDayLog.ReviewDate.ToLocalTime()
                            : sameDayLog.ReviewDate;
                        string oldTimeStr = existingLocalDate.ToString("yyyyMMddHHmmss");

                        if (localReviewDate >= existingLocalDate)
                        {
                            if (oldTimeStr != timeStr)
                            {
                                string oldTombKey = $"{cleanWordKey}_{oldTimeStr}";
                                deletedLogKeys.Add(oldTombKey);
                                await _dbService.AddTombstoneAsync("review_log", oldTombKey, DateTime.Now);
                                existingLogsByKey.Remove($"{localWordId}_{oldTimeStr}");
                            }

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
                        else
                        {
                            // 本地同日记录更新，淘汰传入的同日较早记录并记录墓碑
                            deletedLogKeys.Add(tombKey);
                            await _dbService.AddTombstoneAsync("review_log", tombKey, DateTime.Now);
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
            }

            // 4. 复习日志与 FSRS 状态重演自愈（解决双端跨日离线各自打卡后的状态与日志链分裂）：
            var allLogs = await _dbService.GetAllReviewLogsAsync();
            var logsByWordId = allLogs
                .GroupBy(l => l.WordId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.ReviewDate).ThenBy(l => l.Id).ToList());
            var wordsToCheck = await _dbService.GetAllWordsAsync();

            foreach (var w in wordsToCheck)
            {
                if (w.State != (int)WordLearningState.New && logsByWordId.TryGetValue(w.Id, out var wLogs) && wLogs.Count > 0)
                {
                    bool needsReplay = w.Reps != wLogs.Count;
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
                            int rSched = Math.Max(1, (int)Math.Round((rNext.Date - rLog.ReviewDate.Date).TotalDays));

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
