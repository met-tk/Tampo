using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Models;
using NihongoVocab.Services;
using SQLite;
using Xunit;

namespace NihongoVocab.Tests
{
    /// <summary>
    /// Milestone 1: 数据持久化、配对会话、Token鉴权与SQLite并发韧性测试套件
    /// </summary>
    [Collection("CrossProcessEvent")]
    public class SyncPersistenceTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();

        private string CreateTempDbPath()
        {
            string path = Path.Combine(Path.GetTempPath(), $"tampo_m1_test_{Guid.NewGuid():N}.db");
            _tempFiles.Add(path);
            return path;
        }

        public void Dispose()
        {
            SQLiteAsyncConnection.ResetPool();
            foreach (var file in _tempFiles)
            {
                CleanupDb(file);
            }
        }

        private static void CleanupDb(string dbPath)
        {
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                string wal = dbPath + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                string shm = dbPath + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch { }
        }

        #region Suite 1: PairedDevices CRUD & Constraints

        [Fact]
        public async Task PairedDevices_SaveAndRetrieve_ReturnsMatchingDevice()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            var device = new PairedDevice
            {
                Id = "android-device-uuid-001",
                DeviceName = "Pixel 8 Pro",
                AuthToken = "a1b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abcdef0",
                ClientIp = "192.168.1.105",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            bool saved = await db.SavePairedDeviceAsync(device);
            Assert.True(saved);

            var list = await db.GetPairedDevicesAsync();
            Assert.NotNull(list);
            Assert.Single(list);
            Assert.Equal("android-device-uuid-001", list[0].Id);
            Assert.Equal("Pixel 8 Pro", list[0].DeviceName);

            var byToken = await db.GetDeviceByTokenAsync(device.AuthToken);
            Assert.NotNull(byToken);
            Assert.Equal("android-device-uuid-001", byToken.Id);
            Assert.Equal("192.168.1.105", byToken.ClientIp);
        }

        [Fact]
        public async Task PairedDevices_UpdateLastSeen_UpdatesTimestampAndIpWithoutModifyingOthers()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            long t0 = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100;
            var device = new PairedDevice
            {
                Id = "device-lastseen-test",
                DeviceName = "Galaxy S24",
                AuthToken = "test_token_lastseen_1234567890abcdef1234567890abcdef1234567890abcdef",
                ClientIp = "192.168.1.50",
                CreatedAt = t0,
                LastSeenAt = t0
            };
            await db.SavePairedDeviceAsync(device);

            await Task.Delay(50);
            bool updated = await db.UpdateLastSeenAsync(device.AuthToken, "192.168.1.88");
            Assert.True(updated);

            var fetched = await db.GetDeviceByTokenAsync(device.AuthToken);
            Assert.NotNull(fetched);
            Assert.Equal("192.168.1.88", fetched.ClientIp);
            Assert.True(fetched.LastSeenAt >= t0);
            Assert.Equal("Galaxy S24", fetched.DeviceName);
            Assert.Equal(t0, fetched.CreatedAt);
        }

        [Fact]
        public async Task PairedDevices_DeleteDevice_RemovesDeviceAndInvalidatesToken()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            var dev1 = new PairedDevice
            {
                Id = "dev-1",
                DeviceName = "Phone 1",
                AuthToken = "token_one_1111111111111111111111111111111111111111111111111111111111",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            var dev2 = new PairedDevice
            {
                Id = "dev-2",
                DeviceName = "Phone 2",
                AuthToken = "token_two_2222222222222222222222222222222222222222222222222222222222",
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await db.SavePairedDeviceAsync(dev1);
            await db.SavePairedDeviceAsync(dev2);

            bool deleted = await db.DeletePairedDeviceAsync("dev-1");
            Assert.True(deleted);

            var list = await db.GetPairedDevicesAsync();
            Assert.Single(list);
            Assert.Equal("dev-2", list[0].Id);

            var token1Lookup = await db.GetDeviceByTokenAsync(dev1.AuthToken);
            Assert.Null(token1Lookup);

            var token2Lookup = await db.GetDeviceByTokenAsync(dev2.AuthToken);
            Assert.NotNull(token2Lookup);
        }

        [Fact]
        public async Task PairedDevices_UniqueTokenConstraint_PreventsDuplicateTokens()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            string commonToken = "duplicate_token_value_0000000000000000000000000000000000000000000";
            var dev1 = new PairedDevice
            {
                Id = "dev-dup-1",
                DeviceName = "Device 1",
                AuthToken = commonToken,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await db.SavePairedDeviceAsync(dev1);

            var dev2 = new PairedDevice
            {
                Id = "dev-dup-2",
                DeviceName = "Device 2",
                AuthToken = commonToken, // 重复 Token
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await db.SavePairedDeviceAsync(dev2);
            });
        }

        [Fact]
        public async Task PairedDevices_GetAllPairedDevices_ReturnsOrderedList()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await db.SavePairedDeviceAsync(new PairedDevice { Id = "d1", DeviceName = "Old", AuthToken = "tok_1", LastSeenAt = now - 200, CreatedAt = now - 200 });
            await db.SavePairedDeviceAsync(new PairedDevice { Id = "d2", DeviceName = "Newest", AuthToken = "tok_2", LastSeenAt = now, CreatedAt = now });
            await db.SavePairedDeviceAsync(new PairedDevice { Id = "d3", DeviceName = "Mid", AuthToken = "tok_3", LastSeenAt = now - 50, CreatedAt = now - 50 });

            var list = await db.GetPairedDevicesAsync();
            Assert.Equal(3, list.Count);
            Assert.Equal("d2", list[0].Id); // 最新活跃排在最前
        }

        #endregion

        #region Suite 2: PairingSessions Temporary Codes & Lifecycle

        [Fact]
        public async Task PairingSessions_CreatePairingCode_Generates6DigitCodeWith5MinExpiry()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string code = await db.CreatePairingCodeAsync();
            long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Assert.NotNull(code);
            Assert.Equal(6, code.Length);
            Assert.True(int.TryParse(code, out int n));
            Assert.InRange(n, 100000, 999999);

            var session = await db.GetPairingSessionAsync(code);
            Assert.NotNull(session);
            Assert.Equal(code, session.Code);
            Assert.InRange(session.ExpiresAt, before + 295, after + 305);
        }

        [Fact]
        public async Task PairingSessions_ValidateAndConsume_ValidCode_SucceedsOnce()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            string code = await db.CreatePairingCodeAsync();

            // 首次核销必须成功
            bool firstResult = await db.ValidateAndConsumePairingCodeAsync(code);
            Assert.True(firstResult);

            // 二次核销必须被拒（单次消费原则，防重放）
            bool secondResult = await db.ValidateAndConsumePairingCodeAsync(code);
            Assert.False(secondResult);
        }

        [Fact]
        public async Task PairingSessions_MultiConnectionConcurrentConsumption_OnlyOneWins()
        {
            // 1. 初始化独立测试数据库并生成有效 5 分钟临时配对码
            string dbPath = CreateTempDbPath();
            var masterDb = new DatabaseService(dbPath);
            await masterDb.InitializeAsync();

            string code = await masterDb.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            Assert.NotNull(code);
            Assert.Equal(6, code.Length);

            // 2. 派生 10 组携带 PrivateCache 标志的独立 DatabaseService 连接（模拟多物理进程/独立客户端并发）
            const int connectionCount = 10;
            var services = new List<DatabaseService>(connectionCount);
            try
            {
                for (int i = 0; i < connectionCount; i++)
                {
                    var flags = SQLiteOpenFlags.ReadWrite 
                              | SQLiteOpenFlags.Create 
                              | SQLiteOpenFlags.FullMutex 
                              | SQLiteOpenFlags.PrivateCache;
                    var svc = new DatabaseService(dbPath, flags);
                    await svc.InitializeAsync();
                    services.Add(svc);
                }

                // 3. 构建起跑门禁（Starting Gate），消除线程调度抖动，令 10 个连接在同一时刻发起并发核销
                var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var tasks = services.Select(svc => Task.Run(async () =>
                {
                    await startGate.Task;
                    return await svc.ValidateAndConsumePairingCodeAsync(code);
                })).ToArray();

                // 统一开闸释放并发流量
                startGate.SetResult(true);
                bool[] results = await Task.WhenAll(tasks);

                // 4. 严苛断言：全量 10 个核销请求中，严格仅有 1 个返回 true，其余 9 个全数返回 false
                Assert.NotNull(results);
                Assert.Equal(connectionCount, results.Length);

                int successCount = results.Count(r => r);
                int failureCount = results.Count(r => !r);

                Assert.Equal(1, successCount);
                Assert.Equal(9, failureCount);

                // 5. 校验数据库最终一致性：配对码记录已彻底销毁，后续任意核销均返回 false
                var sessionInDb = await masterDb.GetPairingSessionAsync(code);
                Assert.Null(sessionInDb);

                bool subsequentAttempt = await masterDb.ValidateAndConsumePairingCodeAsync(code);
                Assert.False(subsequentAttempt);
            }
            finally
            {
                foreach (var svc in services)
                {
                    try { await svc.CloseAsync(); } catch { }
                }
                try { await masterDb.CloseAsync(); } catch { }
                SQLiteAsyncConnection.ResetPool();
            }
        }

        [Fact]
        public async Task PairingSessions_ValidateAndConsume_ExpiredCode_ReturnsFalse()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 创建一个已过期的配对码（有效期设为负数）
            string expiredCode = await db.CreatePairingCodeAsync(validity: TimeSpan.FromSeconds(-10));

            bool result = await db.ValidateAndConsumePairingCodeAsync(expiredCode);
            Assert.False(result);
        }

        [Theory]
        [InlineData("000000")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abcdef")]
        [InlineData("12345")]
        public async Task PairingSessions_ValidateAndConsume_InvalidOrNonexistentCode_ReturnsFalse(string invalidCode)
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            bool result = await db.ValidateAndConsumePairingCodeAsync(invalidCode);
            Assert.False(result);
        }

        [Fact]
        public async Task PairingSessions_CleanupExpiredCodes_RemovesOnlyExpiredSessions()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            string validCode = await db.CreatePairingCodeAsync(TimeSpan.FromMinutes(5));
            string expired1 = await db.CreatePairingCodeAsync(TimeSpan.FromSeconds(-50));
            string expired2 = await db.CreatePairingCodeAsync(TimeSpan.FromSeconds(-100));

            int cleaned = await db.CleanupExpiredCodesAsync();
            Assert.True(cleaned >= 2);

            Assert.True(await db.ValidateAndConsumePairingCodeAsync(validCode));
            Assert.False(await db.ValidateAndConsumePairingCodeAsync(expired1));
        }

        #endregion

        #region Suite 3: Token Auth & 401 Simulation

        [Fact]
        public async Task TokenAuth_NonExistentToken_ReturnsNullAndSimulates401()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            var device = await db.GetDeviceByTokenAsync("non_existent_token_123");
            Assert.Null(device);
        }

        [Fact]
        public async Task TokenAuth_RevokedToken_Returns401Immediately()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            string token = "token_to_be_revoked_999";
            var device = new PairedDevice
            {
                Id = "dev-revoked",
                DeviceName = "Test Mobile",
                AuthToken = token,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            await db.SavePairedDeviceAsync(device);

            Assert.NotNull(await db.GetDeviceByTokenAsync(token));

            // 管理员解绑
            await db.DeletePairedDeviceAsync("dev-revoked");

            // 再次鉴权必须失败
            var postRevocation = await db.GetDeviceByTokenAsync(token);
            Assert.Null(postRevocation);
        }

        [Fact]
        public async Task TokenAuth_ValidToken_ReturnsDeviceAndUpdatesLastSeen()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            string token = "token_valid_active_777";
            long initTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 30;
            var device = new PairedDevice
            {
                Id = "dev-active",
                DeviceName = "Active Mobile",
                AuthToken = token,
                ClientIp = "192.168.1.110",
                CreatedAt = initTime,
                LastSeenAt = initTime
            };
            await db.SavePairedDeviceAsync(device);

            var authDevice = await db.GetDeviceByTokenAsync(token);
            Assert.NotNull(authDevice);

            await db.UpdateLastSeenAsync(token, "192.168.1.120");
            var updated = await db.GetDeviceByTokenAsync(token);
            Assert.NotNull(updated);
            Assert.Equal("192.168.1.120", updated.ClientIp);
            Assert.True(updated.LastSeenAt >= initTime);
        }

        #endregion

        #region Suite 4: PRAGMA busy_timeout & Concurrency Resilience

        [Fact]
        public async Task Pragma_Configuration_VerifiesWAL_NormalSync_And_BusyTimeout5000()
        {
            string dbPath = CreateTempDbPath();
            var db = new DatabaseService(dbPath);
            await db.InitializeAsync();

            // 通过 ExecuteScalarAsync 直接向 SQLite 引擎检验 PRAGMA 设置
            string journalMode = await db.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
            int syncMode = await db.ExecuteScalarAsync<int>("PRAGMA synchronous;");
            int busyTimeout = await db.ExecuteScalarAsync<int>("PRAGMA busy_timeout;");

            Assert.Equal("wal", journalMode.ToLowerInvariant());
            Assert.Equal(1, syncMode); // 1 = NORMAL
            Assert.Equal(5000, busyTimeout); // 5000 毫秒超时重试
        }

        [Fact]
        public async Task Concurrency_MultiConnection_ConcurrentWrites_DoNotThrowSqliteBusy()
        {
            string sharedDbPath = CreateTempDbPath();

            // 实例化两组独立的连接（模拟主程序 Tampo.exe 与后台服务 TampoSyncService.exe）
            var db1 = new DatabaseService(sharedDbPath);
            var db2 = new DatabaseService(sharedDbPath);

            await db1.InitializeAsync();
            await db2.InitializeAsync();

            // 准备高频并发写入操作
            var tasks = new List<Task>();
            const int iterations = 15;

            // Connection 1 写入单词
            for (int i = 0; i < iterations; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var word = new Word
                    {
                        Text = $"并发测试词_{index}_{Guid.NewGuid():N}",
                        CreatedAt = DateTime.Now,
                        State = 0
                    };
                    await db1.AddWordAsync(word);
                }));
            }

            // Connection 2 写入已配对设备与配对会话
            for (int i = 0; i < iterations; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    await db2.CreatePairingCodeAsync();
                    await db2.SavePairedDeviceAsync(new PairedDevice
                    {
                        Id = $"concurrent-dev-{index}",
                        DeviceName = $"Device {index}",
                        AuthToken = $"token_concurrent_{index}_{Guid.NewGuid():N}",
                        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        LastSeenAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });
                }));
            }

            // 执行所有并发任务，绝不发生锁表崩溃 (SQLITE_BUSY)
            var aggregate = Task.WhenAll(tasks);
            await aggregate;

            Assert.True(aggregate.IsCompletedSuccessfully);

            // 验证双端均可读取完整数据
            var words = await db2.GetAllWordsAsync();
            var devices = await db1.GetPairedDevicesAsync();

            Assert.Equal(iterations, words.Count);
            Assert.Equal(iterations, devices.Count);
        }

        [Fact]
        public async Task Concurrency_SimultaneousReadWhileWriting_DoesNotBlockOrDeadlock()
        {
            string sharedDbPath = CreateTempDbPath();
            var dbWriter = new DatabaseService(sharedDbPath);
            var dbReader = new DatabaseService(sharedDbPath);

            await dbWriter.InitializeAsync();
            await dbReader.InitializeAsync();

            // 预植入初始数据
            await dbWriter.AddWordAsync(new Word { Text = "基准词", CreatedAt = DateTime.Now });

            var writeTask = Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await dbWriter.AddWordAsync(new Word { Text = $"写压力词_{i}", CreatedAt = DateTime.Now });
                    await Task.Delay(5);
                }
            });

            var readTask = Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var all = await dbReader.GetAllWordsAsync();
                    Assert.NotEmpty(all);
                    await Task.Delay(5);
                }
            });

            await Task.WhenAll(writeTask, readTask);
        }

        #endregion

        #region Suite 5: Cross-Process Signaling & data_version

        [Fact]
        public void CrossProcessEvent_NamedEvent_TriggersAndSignalsWaitHandle()
        {
            string eventName = $@"Local\Tampo_Test_SyncPersist_{Guid.NewGuid():N}";
            using var waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);

            bool signaled = false;
            var waitThread = new Thread(() =>
            {
                signaled = waitHandle.WaitOne(TimeSpan.FromSeconds(2));
            });
            waitThread.Start();

            Thread.Sleep(50);
            waitHandle.Set(); // 触发信号

            waitThread.Join(2000);
            Assert.True(signaled);

            // AutoReset 验证：后续等待应超时
            bool secondWait = waitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
            Assert.False(secondWait);
        }

        [Fact]
        public async Task PragmaDataVersion_ExternalWrite_IncrementsDataVersion()
        {
            string sharedDbPath = CreateTempDbPath();
            var dbConn1 = new DatabaseService(sharedDbPath);
            var dbConn2 = new DatabaseService(sharedDbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);

            await dbConn1.InitializeAsync();
            await dbConn2.InitializeAsync();

            // Conn1 获取初始 data_version
            int v1 = await dbConn1.ExecuteScalarAsync<int>("PRAGMA data_version;");

            // Conn2 写入新数据并提交
            await dbConn2.AddWordAsync(new Word { Text = "变动版本测试词", CreatedAt = DateTime.Now });

            // Conn1 再次读取 data_version
            int v2 = await dbConn1.ExecuteScalarAsync<int>("PRAGMA data_version;");

            // 外部写入必须导致 data_version 递增
            Assert.True(v2 > v1, $"Conn1 的 data_version 应从 {v1} 递增，实际为: {v2}");
        }

        #endregion
    }
}
