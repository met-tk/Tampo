using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.SyncService;
using SQLite;
using Xunit;

namespace NihongoVocab.Tests
{
    /// <summary>
    /// Milestone 2: Windows 后台守护服务、生命周期管控、配对换票与鉴权安全单元测试
    /// </summary>
    [Collection("CrossProcessEvent")]
    public class SyncDaemonTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        private string CreateTempDbPath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"tampo_daemon_test_{Guid.NewGuid():N}.db");
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            SQLiteAsyncConnection.ResetPool();
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file)) File.Delete(file);
                    string wal = file + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    string shm = file + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                }
                catch { }
            }
        }

        #region 1. SyncServiceController 注册表开机自启测试

        [Fact]
        public void SyncServiceController_Autostart_SetAndVerifyQuotes_CleanlyRemoves()
        {
            if (!OperatingSystem.IsWindows()) return;

            var controller = new SyncServiceController();

            // 1. 启用开机自启
            bool setSuccess = controller.SetAutostart(true);
            Assert.True(setSuccess, "SetAutostart(true) 应该成功执行");
            Assert.True(controller.IsAutostartEnabled(), "IsAutostartEnabled() 应该返回 true");

            // 2. 直接读取底层 Windows 注册表验证双引号包裹（防空格截断漏洞）
            using (var key = Registry.CurrentUser.OpenSubKey(SyncServiceController.AutostartRegistryKey, false))
            {
                Assert.NotNull(key);
                object? val = key.GetValue(SyncServiceController.AutostartValueName);
                Assert.NotNull(val);
                string cmd = val.ToString()!;
                Assert.StartsWith("\"", cmd);
                Assert.EndsWith("\"", cmd);
                Assert.Contains("TampoSyncService.exe", cmd);
            }

            // 3. 关闭开机自启
            bool removeSuccess = controller.SetAutostart(false);
            Assert.True(removeSuccess, "SetAutostart(false) 应该成功执行");
            Assert.False(controller.IsAutostartEnabled(), "IsAutostartEnabled() 应该返回 false");

            using (var key = Registry.CurrentUser.OpenSubKey(SyncServiceController.AutostartRegistryKey, false))
            {
                Assert.NotNull(key);
                object? val = key.GetValue(SyncServiceController.AutostartValueName);
                Assert.Null(val);
            }
        }

        #endregion

        #region 2. Ping 协议与报文解析测试

        [Fact]
        public void SyncDaemon_PingPayload_DeserializesAndValidatesStructure()
        {
            var pingObj = new
            {
                status = "ok",
                service = "TampoSyncService",
                version = "1.5.2",
                name = "DESKTOP-TEST"
            };

            string json = JsonSerializer.Serialize(pingObj);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.Equal("TampoSyncService", root.GetProperty("service").GetString());
            Assert.Equal("1.5.2", root.GetProperty("version").GetString());
            Assert.Equal("DESKTOP-TEST", root.GetProperty("name").GetString());
        }

        #endregion

        #region 3. 配对报文与别名兼容性测试

        private class TestPairingRequest
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }

            [JsonPropertyName("pairingCode")]
            public string? PairingCode { get; set; }

            [JsonPropertyName("deviceName")]
            public string? DeviceName { get; set; }

            [JsonPropertyName("deviceId")]
            public string? DeviceId { get; set; }

            public string GetEffectiveCode() => !string.IsNullOrWhiteSpace(Code)
                ? Code.Trim()
                : (PairingCode?.Trim() ?? string.Empty);
        }

        [Theory]
        [InlineData("{\"code\":\"123456\"}", "123456")]
        [InlineData("{\"pairingCode\":\"654321\"}", "654321")]
        [InlineData("{\"code\":\"  998877  \",\"pairingCode\":\"112233\"}", "998877")]
        [InlineData("{\"deviceName\":\"Pixel 8\"}", "")]
        public void SyncDaemon_PairingRequest_CodeResolution_HandlesFieldAliases(string json, string expectedCode)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var req = JsonSerializer.Deserialize<TestPairingRequest>(json, options);

            Assert.NotNull(req);
            Assert.Equal(expectedCode, req.GetEffectiveCode());
        }

        #endregion

        #region 4. 配对码生命周期与原子单步核销测试

        [Fact]
        public async Task SyncDaemon_PairingSession_CreateAndConsumeAtomic()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 1. 生成 5 分钟有效期的 6 位配对码
            string code = await db.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            Assert.Equal(6, code.Length);
            Assert.True(int.TryParse(code, out _), "配对码必须为全数字");

            // 2. 首次核销必须成功
            bool firstConsume = await db.ValidateAndConsumePairingCodeAsync(code);
            Assert.True(firstConsume, "首次核销必须成功返回 true");

            // 3. 再次核销必须失败（单次消费有效，彻底杜绝重放）
            bool secondConsume = await db.ValidateAndConsumePairingCodeAsync(code);
            Assert.False(secondConsume, "配对码已被消费，再次核销必须返回 false");
        }

        [Fact]
        public async Task SyncDaemon_PairingSession_ExpiredCode_Rejected()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 生成已过期的配对会话 (负值 validity 使 ExpiresAt < now)
            string expiredCode = await db.CreatePairingCodeAsync(TimeSpan.FromSeconds(-10));

            // 核销已过期码必须返回 false
            bool consumed = await db.ValidateAndConsumePairingCodeAsync(expiredCode);
            Assert.False(consumed, "已过期的配对码必须被拒绝核销");
        }

        #endregion

        #region 5. 长效 AuthToken 生成与设备管理安全测试

        [Fact]
        public async Task SyncDaemon_PairedDevice_TokenAuthentication_Hex64Verification()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 1. 生成 256 位高强度安全 Token (32 字节 -> 64 字符十六进制)
            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
            string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
            Assert.Equal(64, token.Length);

            var device = new PairedDevice
            {
                Id = Guid.NewGuid().ToString(),
                DeviceName = "Pixel 8 Pro",
                AuthToken = token,
                ClientIp = "192.168.1.108",
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            bool saved = await db.SavePairedDeviceAsync(device);
            Assert.True(saved);

            // 2. 基于 Token 查询鉴权
            var found = await db.GetDeviceByTokenAsync(token);
            Assert.NotNull(found);
            Assert.Equal("Pixel 8 Pro", found.DeviceName);
            Assert.Equal(token, found.AuthToken);

            // 3. 电脑端解绑该设备
            bool deleted = await db.DeletePairedDeviceAsync(device.Id);
            Assert.True(deleted);

            // 4. 解绑后再次以相同 Token 鉴权，必须返回 null (对应 HTTP 401 Unauthorized)
            var revoked = await db.GetDeviceByTokenAsync(token);
            Assert.Null(revoked);
        }

        #endregion

        #region 6. Token 鉴权 Header 提取逻辑测试

        [Theory]
        [InlineData("Bearer d98e72a819b34821a91e57c6109348b5934a123bcdef0123456789abcdef0123", "d98e72a819b34821a91e57c6109348b5934a123bcdef0123456789abcdef0123")]
        [InlineData("bearer   abc123token   ", "abc123token")]
        [InlineData("d98e72a819b34821a91e57c6109348b5934a123bcdef0123456789abcdef0123", "d98e72a819b34821a91e57c6109348b5934a123bcdef0123456789abcdef0123")]
        public void SyncDaemon_TokenHeaderExtraction_ExtractsBearerCleanly(string rawAuthHeader, string expectedToken)
        {
            string? extracted = ExtractTokenFromHeader(rawAuthHeader, null, null);
            Assert.Equal(expectedToken, extracted);
        }

        [Fact]
        public void SyncDaemon_TokenHeaderExtraction_FallbackToCustomHeaderAndQuery()
        {
            // X-Sync-Token 回退
            string? t1 = ExtractTokenFromHeader(null, "custom_x_sync_token", null);
            Assert.Equal("custom_x_sync_token", t1);

            // Query parameter 回退
            string? t2 = ExtractTokenFromHeader(null, null, "query_token_123");
            Assert.Equal("query_token_123", t2);

            // 全空时返回 null
            string? t3 = ExtractTokenFromHeader(null, null, null);
            Assert.Null(t3);
        }

        private static string? ExtractTokenFromHeader(string? authHeader, string? syncTokenHeader, string? queryToken)
        {
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                string trimmed = authHeader.Trim();
                if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    string t = trimmed.Substring(7).Trim();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
                else if (!trimmed.Contains(' '))
                {
                    return trimmed;
                }
            }

            if (!string.IsNullOrWhiteSpace(syncTokenHeader))
            {
                return syncTokenHeader.Trim();
            }

            if (!string.IsNullOrWhiteSpace(queryToken))
            {
                return queryToken.Trim();
            }

            return null;
        }

        #endregion

        #region 7. 回环地址安全管控与网络隔离测试

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("::1", true)]
        [InlineData("192.168.1.100", false)]
        [InlineData("10.0.0.2", false)]
        [InlineData("172.16.0.1", false)]
        public void SyncDaemon_LoopbackSecurity_RestrictsStopEndpoint(string ipString, bool expectedLoopback)
        {
            var ip = IPAddress.Parse(ipString);
            bool isLoopback = IPAddress.IsLoopback(ip);
            Assert.Equal(expectedLoopback, isLoopback);
        }

        #endregion

        #region 8. FlexibleBoolConverter 反序列化混用测试

        private class TestBoolEntity
        {
            [JsonConverter(typeof(FlexibleBoolConverter))]
            public bool IsActive { get; set; }
        }

        [Theory]
        [InlineData("{\"IsActive\": true}", true)]
        [InlineData("{\"IsActive\": false}", false)]
        [InlineData("{\"IsActive\": 1}", true)]
        [InlineData("{\"IsActive\": 0}", false)]
        [InlineData("{\"IsActive\": \"true\"}", true)]
        [InlineData("{\"IsActive\": \"false\"}", false)]
        [InlineData("{\"IsActive\": \"1\"}", true)]
        [InlineData("{\"IsActive\": \"0\"}", false)]
        public void SyncDaemon_FlexibleBoolConverter_DeserializesMultipleTypes(string json, bool expected)
        {
            var entity = JsonSerializer.Deserialize<TestBoolEntity>(json);
            Assert.NotNull(entity);
            Assert.Equal(expected, entity.IsActive);
        }

        #endregion

        #region 9. 真实 HttpSyncServer 端到端集成测试 (Live Integration Tests)

        private static int GetAvailablePort(int startingPort = 52088)
        {
            for (int port = startingPort; port < startingPort + 200; port++)
            {
                try
                {
                    using var socket = new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp);
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    socket.Close();
                    return port;
                }
                catch { }
            }
            return startingPort;
        }

        [Fact]
        public async Task HttpSyncServer_StartAndFallback_BindsLoopbackWithoutCrashing()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            using var shutdownCts = new CancellationTokenSource();
            int testPort = GetAvailablePort(52088);
            using var server = new HttpSyncServer(db, shutdownCts, testPort);

            // 1. 验证启动与降级绑定无异常
            server.Start();
            Assert.True(server.IsRunning, "HttpSyncServer.Start() 执行后 IsRunning 必须为 true");

            // 2. 发送真实 HTTP GET /api/ping 探针请求
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"http://127.0.0.1:{testPort}/api/ping");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.Equal("TampoSyncService", root.GetProperty("service").GetString());
            Assert.Equal("1.5.2", root.GetProperty("version").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("name").GetString()), "主机名称不能为空");

            // 3. 停止服务并验证运行状态
            server.Stop();
            Assert.False(server.IsRunning, "HttpSyncServer.Stop() 后 IsRunning 必须为 false");
        }

        [Fact]
        public async Task HttpSyncServer_PairEndpoint_ValidCodeReturnsToken()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            using var shutdownCts = new CancellationTokenSource();
            int testPort = GetAvailablePort(52089);
            using var server = new HttpSyncServer(db, shutdownCts, testPort);
            server.Start();
            Assert.True(server.IsRunning);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // 1. 生成 5 分钟有效期的 6 位临时配对码
            string validCode = await db.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            Assert.Equal(6, validCode.Length);

            // 2. 发起 POST /api/pair 请求
            var pairPayload = new
            {
                code = validCode,
                deviceName = "Pixel 8 Pro (Test)",
                deviceId = "device-uuid-test-999"
            };
            var content = new StringContent(JsonSerializer.Serialize(pairPayload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://127.0.0.1:{testPort}/api/pair", content);

            // 3. 验证 200 OK 与长效 64 字符十六进制 AuthToken
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("paired", root.GetProperty("status").GetString());
            string token = root.GetProperty("token").GetString()!;
            Assert.NotNull(token);
            Assert.Equal(64, token.Length);
            Assert.Matches("^[0-9a-f]{64}$", token);

            // 4. 验证 SQLite 底层 PairedDevices 表已成功写入
            var savedDevice = await db.GetDeviceByTokenAsync(token);
            Assert.NotNull(savedDevice);
            Assert.Equal("Pixel 8 Pro (Test)", savedDevice.DeviceName);
            Assert.Equal("device-uuid-test-999", savedDevice.Id);

            // 5. 验证配对码已被核销，再次请求必须返回 403 Forbidden
            var replayContent = new StringContent(JsonSerializer.Serialize(pairPayload), Encoding.UTF8, "application/json");
            var replayResponse = await client.PostAsync($"http://127.0.0.1:{testPort}/api/pair", replayContent);
            Assert.Equal(HttpStatusCode.Forbidden, replayResponse.StatusCode);

            server.Stop();
        }

        [Fact]
        public async Task HttpSyncServer_PairEndpoint_LimitsRepeatedInvalidCodes()
        {
            var db = new DatabaseService(CreateTempDbPath());
            await db.InitializeAsync();
            using var shutdownCts = new CancellationTokenSource();
            int port = GetAvailablePort(52102);
            using var server = new HttpSyncServer(db, shutdownCts, port);
            server.Start();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            for (int i = 0; i < 5; i++)
            {
                using var content = new StringContent("{\"code\":\"000000\"}", Encoding.UTF8, "application/json");
                using var response = await client.PostAsync($"http://127.0.0.1:{port}/api/pair", content);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            string validCode = await db.CreatePairingCodeAsync();
            using var validContent = new StringContent($"{{\"code\":\"{validCode}\"}}", Encoding.UTF8, "application/json");
            using var lockedResponse = await client.PostAsync($"http://127.0.0.1:{port}/api/pair", validContent);
            Assert.Equal(HttpStatusCode.TooManyRequests, lockedResponse.StatusCode);
            Assert.NotNull(await db.GetPairingSessionAsync(validCode));
        }

        [Fact]
        public async Task HttpSyncServer_UnauthenticatedPush_RejectsBeforeReadingLargeBody()
        {
            var db = new DatabaseService(CreateTempDbPath());
            await db.InitializeAsync();
            using var shutdownCts = new CancellationTokenSource();
            int port = GetAvailablePort(52103);
            using var server = new HttpSyncServer(db, shutdownCts, port);
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            byte[] request = Encoding.ASCII.GetBytes(
                "POST /api/sync/push HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 52428800\r\n\r\n");
            await stream.WriteAsync(request);
            byte[] response = new byte[256];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            int read = await stream.ReadAsync(response, timeout.Token);
            Assert.StartsWith("HTTP/1.1 401", Encoding.ASCII.GetString(response, 0, read));
        }

        [Fact]
        public async Task HttpSyncServer_LocalControl_RejectsBrowserOrigin()
        {
            var db = new DatabaseService(CreateTempDbPath());
            await db.InitializeAsync();
            using var shutdownCts = new CancellationTokenSource();
            int port = GetAvailablePort(52104);
            using var server = new HttpSyncServer(db, shutdownCts, port);
            server.Start();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/control/stop");
            request.Headers.TryAddWithoutValidation("Origin", "https://example.com");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(shutdownCts.IsCancellationRequested);

            using var getResponse = await client.GetAsync($"http://127.0.0.1:{port}/api/control/maintenance-mode?enabled=true");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        }

        [Fact]
        public async Task HttpSyncServer_ControlStop_LoopbackAccepted()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            using var shutdownCts = new CancellationTokenSource();
            int testPort = GetAvailablePort(52090);
            using var server = new HttpSyncServer(db, shutdownCts, testPort);
            server.Start();
            Assert.True(server.IsRunning);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // 1. 本机回环发送 POST /api/control/stop
            var stopContent = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://127.0.0.1:{testPort}/api/control/stop", stopContent);

            // 2. 验证响应为 200 OK 且 status 为 stopping
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("stopping", root.GetProperty("status").GetString());

            // 3. 验证协同式关停信号已触发
            bool cancelled = false;
            for (int i = 0; i < 20; i++)
            {
                if (shutdownCts.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                await Task.Delay(50);
            }
            Assert.True(cancelled, "接收到回环关停指令后，shutdownCts 应该在延时后被置为 Cancelled");

            server.Stop();
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task HttpSyncServer_MaintenanceMode_LocksAndReturns503_ThenUnlocks()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            using var shutdownCts = new CancellationTokenSource();
            int testPort = GetAvailablePort(52091);
            using var server = new HttpSyncServer(db, shutdownCts, testPort);
            server.Start();

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // 1. 开启维护模式 POST /api/control/maintenance-mode?enabled=true
            var lockRes = await client.PostAsync($"http://127.0.0.1:{testPort}/api/control/maintenance-mode?enabled=true", null);
            Assert.Equal(HttpStatusCode.OK, lockRes.StatusCode);

            // 2. 模拟客户端发送 POST /api/sync/pull，应当被 503 拦截
            var pullRes = await client.PostAsync($"http://127.0.0.1:{testPort}/api/sync/pull", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, pullRes.StatusCode);

            // 3. 关闭维护模式 POST /api/control/maintenance-mode?enabled=false
            var unlockRes = await client.PostAsync($"http://127.0.0.1:{testPort}/api/control/maintenance-mode?enabled=false", null);
            Assert.Equal(HttpStatusCode.OK, unlockRes.StatusCode);

            server.Stop();
        }

        [Fact]
        public async Task HttpSyncServer_DevicesStatus_ReturnsLiveAndPairedDevices()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            var dev = new PairedDevice
            {
                Id = "test-dev-1",
                DeviceName = "Mi 13",
                DeviceModel = "Xiaomi 2211133C",
                CustomAlias = "我的主力机",
                AuthToken = "token123"
            };
            await db.SavePairedDeviceAsync(dev);

            using var shutdownCts = new CancellationTokenSource();
            int testPort = GetAvailablePort(52092);
            using var server = new HttpSyncServer(db, shutdownCts, testPort);
            server.Start();

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            var res = await client.GetAsync($"http://127.0.0.1:{testPort}/api/devices/status");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("devices").GetArrayLength() >= 1);

            server.Stop();
        }

        [Fact]
        public async Task DatabaseService_ApplyMaintenanceRestoredEpochAndPromoteTimestamps_GeneratesNewEpochAndUpdatesTimestamps()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 插入测试生词
            var word = new Word
            {
                Text = "テスト",
                StateUpdatedAt = DateTime.UtcNow.AddDays(-10),
                MetaUpdatedAt = DateTime.UtcNow.AddDays(-10)
            };
            await db.AddWordAsync(word);

            string newEpoch = await db.ApplyMaintenanceRestoredEpochAndPromoteTimestampsAsync(DateTime.UtcNow.AddDays(-5));
            Assert.StartsWith("epoch_", newEpoch);

            string? currentEpoch = await db.GetSyncEpochAsync();
            Assert.Equal(newEpoch, currentEpoch);

            // 验证时间戳已提升至当前时刻
            var refreshed = await db.GetWordByIdAsync(word.Id);
            Assert.NotNull(refreshed);
            Assert.True(refreshed.StateUpdatedAt >= DateTime.UtcNow.AddMinutes(-1));
        }

        #endregion
    }
}
