using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NihongoVocab.Models;
using NihongoVocab.Services;
using SQLite;
using Xunit;

namespace NihongoVocab.Tests
{
    [Collection("CrossProcessEvent")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class CrossProcessEventTests : IDisposable
    {
        private readonly string _testDbPath;

        public CrossProcessEventTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"tampo_test_event_{Guid.NewGuid():N}.db");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
                string wal = _testDbPath + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                string shm = _testDbPath + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch { }
        }

        [Fact]
        public void CrossProcessEventHelper_SignalAndReset_Succeeds()
        {
            string testEvent = $@"Local\Tampo_Test_Signal_{Guid.NewGuid():N}";
            using var handle = CrossProcessEventHelper.TryCreateOrOpenEvent(testEvent);
            Assert.NotNull(handle);

            // 触发事件
            bool signaled = CrossProcessEventHelper.Signal(testEvent);
            Assert.True(signaled);

            // AutoReset 验证：第一次等待应立即成功
            bool wait1 = handle!.WaitOne(1000);
            Assert.True(wait1, "第一次 WaitOne 应当被满足");

            // 第二次等待应超时（证明已被自动复位）
            bool wait2 = handle.WaitOne(100);
            Assert.False(wait2, "第二次 WaitOne 应当超时，证明 AutoReset 生效");
        }

        [Fact]
        public async Task DatabaseService_GetDataVersionAsync_ReturnsValidNumber()
        {
            var db = new DatabaseService(_testDbPath);
            await db.InitializeAsync();

            long v1 = await db.GetDataVersionAsync();
            Assert.True(v1 >= 0);
        }

        [Fact]
        public async Task DatabaseService_DataVersion_IncrementsOnExternalWrite()
        {
            // 建立连接 1 (模拟 Tampo.exe)
            var dbConn1 = new DatabaseService(_testDbPath);
            await dbConn1.InitializeAsync();
            long initialVersion = await dbConn1.GetDataVersionAsync();

            // 建立连接 2 (模拟 TampoSyncService.exe 写入)
            var dbConn2 = new DatabaseService(_testDbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await dbConn2.InitializeAsync();

            await dbConn2.AddWordAsync(new Word
            {
                Text = "テスト",
                State = 0
            });

            // 在连接 1 上重新查询 data_version
            long newVersion = await dbConn1.GetDataVersionAsync();

            Assert.True(newVersion > initialVersion, $"连接 1 应检测到 data_version 递增: 之前={initialVersion}, 之后={newVersion}");
        }

        [Fact]
        public async Task SetPreferenceAsync_DoesNotSignalDataChangeWhenValueIsUnchanged()
        {
            var observer = new DatabaseService(_testDbPath);
            var writer = new DatabaseService(_testDbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await observer.InitializeAsync();
            await writer.InitializeAsync();

            await writer.SetPreferenceAsync("AllWords_Dimension", "Month");
            long beforeDuplicate = await observer.GetDataVersionAsync();
            await writer.SetPreferenceAsync("AllWords_Dimension", "Month");
            Assert.Equal(beforeDuplicate, await observer.GetDataVersionAsync());

            await writer.SetPreferenceAsync("AllWords_Dimension", "Week");
            Assert.True(await observer.GetDataVersionAsync() > beforeDuplicate);
        }

        [Fact]
        public async Task SyncContentVersion_ChangesOnlyForWordDataAndSyncEpoch()
        {
            var observer = new DatabaseService(_testDbPath);
            var writer = new DatabaseService(_testDbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await observer.InitializeAsync();
            await writer.InitializeAsync();

            long initialVersion = await observer.GetSyncContentVersionAsync();
            await writer.SetPreferenceAsync("AllWords_Dimension", "Week");
            Assert.Equal(initialVersion, await observer.GetSyncContentVersionAsync());

            var device = new PairedDevice { DeviceName = "Phone", AuthToken = Guid.NewGuid().ToString("N") };
            await writer.SavePairedDeviceAsync(device);
            Assert.True(await writer.UpdateDeviceLastSeenAsync(device.AuthToken, "127.0.0.1"));
            Assert.Equal(initialVersion, await observer.GetSyncContentVersionAsync());

            await writer.AddWordAsync(new Word { Text = "変更" });
            long wordVersion = await observer.GetSyncContentVersionAsync();
            Assert.NotEqual(initialVersion, wordVersion);

            await writer.SetPreferenceAsync("sync_epoch", "epoch_test");
            Assert.NotEqual(wordVersion, await observer.GetSyncContentVersionAsync());
        }

        [Fact]
        public async Task CrossProcessEventListener_TriggersCallbackOnExternalSignal()
        {
            string testEvent = $@"Local\Tampo_Test_Listener_{Guid.NewGuid():N}";
            var dbConn1 = new DatabaseService(_testDbPath);
            await dbConn1.InitializeAsync();

            int callbackInvoked = 0;
            using var listener = new CrossProcessEventListener(dbConn1, () =>
            {
                Interlocked.Increment(ref callbackInvoked);
            }, testEvent);

            await listener.StartAsync();

            // 外部写入新数据
            var dbConn2 = new DatabaseService(_testDbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await dbConn2.InitializeAsync();
            await dbConn2.AddWordAsync(new Word { Text = "信号" });

            // 外部发送通知信号
            CrossProcessEventHelper.Signal(testEvent);

            // 等待后台线程捕捉与处理 (至多 2 秒)
            int waited = 0;
            while (Volatile.Read(ref callbackInvoked) == 0 && waited < 2000)
            {
                await Task.Delay(50);
                waited += 50;
            }

            Assert.True(callbackInvoked > 0, "监听器应当在收到信号且 data_version 变化后触发回调");
        }

        [Fact]
        public async Task CrossProcessEventListener_ServiceMode_IgnoresOwnAndAuxiliaryWrites()
        {
            string testEvent = $@"Local\Tampo_Test_ServiceListener_{Guid.NewGuid():N}";
            var serviceDb = new DatabaseService(_testDbPath);
            var appDb = new DatabaseService(_testDbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await serviceDb.InitializeAsync();
            await appDb.InitializeAsync();

            int callbacks = 0;
            using var listener = new CrossProcessEventListener(serviceDb,
                () => Interlocked.Increment(ref callbacks), testEvent);
            await listener.StartAsync();

            await serviceDb.AddWordAsync(new Word { Text = "服务端写入" });
            CrossProcessEventHelper.Signal(testEvent);
            await Task.Delay(150);
            Assert.Equal(0, Volatile.Read(ref callbacks));

            await appDb.SetPreferenceAsync("AllWords_Dimension", "Week");
            CrossProcessEventHelper.Signal(testEvent);
            await Task.Delay(150);
            Assert.Equal(0, Volatile.Read(ref callbacks));

            await appDb.AddWordAsync(new Word { Text = "桌面端写入" });
            CrossProcessEventHelper.Signal(testEvent);
            for (int waited = 0; Volatile.Read(ref callbacks) == 0 && waited < 2000; waited += 50)
                await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref callbacks));
        }

        [Fact]
        public async Task CrossProcessEventListener_Dispose_ExitsCleanlyWithoutExceptionOrHang()
        {
            string testEvent = $@"Local\Tampo_Test_Dispose_{Guid.NewGuid():N}";
            var db = new DatabaseService(_testDbPath);
            var listener = new CrossProcessEventListener(db, null, testEvent);
            await listener.StartAsync();

            // 稍作等待以确保后台监听线程已进入 WaitAny 阻塞
            Thread.Sleep(50);

            // Act & Assert: Dispose 应当在 500ms 宽限期内迅速完成并干净退出
            var sw = System.Diagnostics.Stopwatch.StartNew();
            listener.Dispose();
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 1000, $"Dispose 应迅速完成退出，实际耗时: {sw.ElapsedMilliseconds}ms");

            // 验证多次 Dispose 的幂等性与安全性
            listener.Dispose();
        }

        [Fact]
        public async Task CrossProcessEventListener_MicroBatchDebounce_HandlesRapidSignalsWithoutDropping()
        {
            string testEvent = $@"Local\Tampo_Test_Debounce_{Guid.NewGuid():N}";
            var dbConn1 = new DatabaseService(_testDbPath);
            await dbConn1.InitializeAsync();

            int callbackCount = 0;
            using var listener = new CrossProcessEventListener(dbConn1, () =>
            {
                Interlocked.Increment(ref callbackCount);
            }, testEvent);

            await listener.StartAsync();

            var dbConn2 = new DatabaseService(_testDbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.PrivateCache);
            await dbConn2.InitializeAsync();

            // 模拟连续密集突发写入与事件触发（5 次快速连续 Signal）
            for (int i = 0; i < 5; i++)
            {
                await dbConn2.AddWordAsync(new Word { Text = $"BurstWord_{i}" });
                CrossProcessEventHelper.Signal(testEvent);
            }

            // 等待微批防抖与处理完成
            await Task.Delay(350);

            // 断言：由于 50ms 防抖合并与 Pending 机制，回调应当被合并触发至少 1 次，且数据版本成功同步
            Assert.True(callbackCount >= 1, "在高频突发事件下，微批防抖机制必须确保变动被即时感知并触发回调");
        }
    }
}
