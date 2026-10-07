using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class LanSyncServiceTests
    {
        [Fact]
        public void NetworkEndpoints_Should_Prioritize_Ethernet_And_Filter_Virtual_Adapters()
        {
            var service = LanSyncService.Instance;
            var endpoints = service.GetNetworkEndpoints();

            Assert.NotNull(endpoints);
            Assert.NotEmpty(endpoints);

            // 验证首选端点不是虚拟适配器
            var primary = endpoints[0];
            Assert.False(primary.IpAddress.StartsWith("127."));
            Assert.False(primary.IpAddress.StartsWith("169.254."));
            Assert.DoesNotContain("vethernet", primary.InterfaceName, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task LanSyncService_Start_Ping_And_Pull_Should_Work()
        {
            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                Assert.True(service.IsRunning);
                using var http = new HttpClient();

                // 1. 测试 Ping
                string pingUrl = $"http://127.0.0.1:{service.Port}/api/ping";
                var pingRes = await http.GetAsync(pingUrl);
                Assert.True(pingRes.IsSuccessStatusCode);

                string pingJson = await pingRes.Content.ReadAsStringAsync();
                using var pingDoc = JsonDocument.Parse(pingJson);
                Assert.Equal("ok", pingDoc.RootElement.GetProperty("status").GetString());

                // 2. 测试未提供 PIN 码拉取（应返回 401）
                string pullUrl = $"http://127.0.0.1:{service.Port}/api/sync/pull";
                var unauthReq = new HttpRequestMessage(HttpMethod.Post, pullUrl);
                var unauthRes = await http.SendAsync(unauthReq);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthRes.StatusCode);

                // 3. 测试提供正确 PIN 码拉取
                var authReq = new HttpRequestMessage(HttpMethod.Post, pullUrl);
                authReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                var authRes = await http.SendAsync(authReq);
                Assert.True(authRes.IsSuccessStatusCode);

                string pullJson = await authRes.Content.ReadAsStringAsync();
                using var pullDoc = JsonDocument.Parse(pullJson);
                Assert.True(pullDoc.RootElement.TryGetProperty("words", out _));
                Assert.True(pullDoc.RootElement.TryGetProperty("wordLists", out _));
            }
            finally
            {
                service.Stop();
                Assert.False(service.IsRunning);
            }
        }

        [Fact]
        public async Task LanSyncService_Push_Mastered_Word_Should_Merge_Correctly()
        {
            var service = LanSyncService.Instance;
            await service.StartAsync();

            var db = new DatabaseService();
            await db.RemoveTombstoneAsync("word", "测试掌握同步词A");
            await db.RemoveTombstoneAsync("word", "测试掌握兼容词B");

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 构造一个标记为掌握的单词（模拟手机端发送 State=3 以及兼容旧版 State=4）
                var payload = new
                {
                    words = new[]
                    {
                        new
                        {
                            text = "测试掌握同步词A",
                            state = 3, // Mastered
                            stability = 0.0,
                            reps = 0,
                            nextReviewDate = (DateTime?)DateTime.Now.AddDays(5),
                            lastReviewDate = (DateTime?)null
                        },
                        new
                        {
                            text = "测试掌握兼容词B",
                            state = 4, // 模拟旧手机端 state=4
                            stability = 3.7145,
                            reps = 1,
                            nextReviewDate = (DateTime?)DateTime.Now.AddDays(4),
                            lastReviewDate = (DateTime?)DateTime.Now
                        }
                    },
                    wordLists = Array.Empty<object>(),
                    reviewLogs = Array.Empty<object>(),
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                // 验证数据库中这两个词已成功落库且 State 均为 Mastered (3)，且 NextReviewDate 置为 null
                var words = await db.GetAllWordsAsync();
                var wordA = words.Find(w => w.Text == "测试掌握同步词A");
                var wordB = words.Find(w => w.Text == "测试掌握兼容词B");

                Assert.NotNull(wordA);
                Assert.Equal(3, wordA.State);
                Assert.Null(wordA.NextReviewDate);
                Assert.Equal(1.0, wordA.CurrentRetrievability);

                Assert.NotNull(wordB);
                Assert.Equal(3, wordB.State);
                Assert.Null(wordB.NextReviewDate);
                Assert.Equal(3.7145, wordB.Stability, 4);

                // 清理测试临时插入的词
                if (wordA != null) await db.DeleteWordsAsync(new[] { wordA.Id });
                if (wordB != null) await db.DeleteWordsAsync(new[] { wordB.Id });
                await db.RemoveTombstoneAsync("word", "测试掌握同步词A");
                await db.RemoveTombstoneAsync("word", "测试掌握兼容词B");
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task LanSyncService_OrthogonalMerge_Should_Preserve_Reviews_While_Updating_WordList()
        {
            var db = new DatabaseService();
            string testWord = "测试正交合并词_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string testList = "测试正交词单_" + Guid.NewGuid().ToString("N").Substring(0, 6);

            await db.RemoveTombstoneAsync("word", testWord);
            await db.RemoveTombstoneAsync("word_list", testList);

            // 1. 本地初始化：电脑端复习了 5 次，StateUpdatedAt 为今天 10:00，无词单归属
            var localWord = new Models.Word
            {
                Text = testWord,
                CreatedAt = DateTime.Now.AddDays(-10),
                State = (int)Models.WordLearningState.Review,
                Reps = 5,
                Stability = 12.0,
                Difficulty = 4.5,
                LastReviewDate = DateTime.Now.AddHours(-2),
                StateUpdatedAt = DateTime.Now.AddHours(-2), // 2小时前打卡
                MetaUpdatedAt = DateTime.Now.AddDays(-10),
                IsInList = false,
                WordListId = null
            };
            await db.AddWordAsync(localWord);

            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 2. 模拟手机端发送数据：
                // 手机端因为尚未同步，Reps 只有 1，StateUpdatedAt 停留在昨天
                // 但是手机端 10 分钟前将该单词分配到了新词单（MetaUpdatedAt 更新为 10 分钟前）
                var payload = new
                {
                    words = new[]
                    {
                        new
                        {
                            text = testWord,
                            state = (int)Models.WordLearningState.Learning,
                            reps = 1, // 旧的复习次数
                            stability = 2.0,
                            difficulty = 5.0,
                            lastReviewDate = DateTime.Now.AddDays(-1),
                            stateUpdatedAt = DateTime.Now.AddDays(-1), // 昨天的旧状态
                            metaUpdatedAt = DateTime.Now.AddMinutes(-10), // 10分钟前的词单归属变更
                            wordListName = testList,
                            isInList = true
                        }
                    },
                    wordLists = new[]
                    {
                        new
                        {
                            name = testList,
                            createdAt = DateTime.Now.AddMinutes(-10),
                            updatedAt = DateTime.Now.AddMinutes(-10)
                        }
                    },
                    reviewLogs = Array.Empty<object>(),
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                // 3. 验证正交合并效果：
                // 学习状态：绝对保留电脑端的 5 次复习和算法记忆状态！
                // 词单归属：采纳手机端的变更，成功归入新词单！
                var refreshedWords = await db.GetAllWordsAsync();
                var merged = refreshedWords.Find(w => w.Text.Equals(testWord, StringComparison.OrdinalIgnoreCase));

                Assert.NotNull(merged);
                Assert.Equal(5, merged.Reps); // 核心：复习次数绝不丢失！
                Assert.Equal(12.0, merged.Stability);
                Assert.Equal((int)Models.WordLearningState.Review, merged.State);
                Assert.True(merged.IsInList); // 核心：词单归属成功合并！
                Assert.Equal(testList, merged.WordListName);

                // 清理
                var allLists = await db.GetAllWordListsAsync();
                var targetL = allLists.Find(l => l.Name.Equals(testList, StringComparison.OrdinalIgnoreCase));
                if (targetL != null) await db.DeleteWordListAsync(targetL.Id);
                await db.DeleteWordsAsync(new[] { merged.Id });
                await db.RemoveTombstoneAsync("word", testWord);
                await db.RemoveTombstoneAsync("word_list", testList);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task LanSyncService_WordList_Tombstone_Should_Prevent_Deleted_List_From_Resurrecting()
        {
            var db = new DatabaseService();
            string deletedListName = "已删除词单_" + Guid.NewGuid().ToString("N").Substring(0, 6);

            // 1. 创建词单后删除之，验证墓碑已产生
            var list = await db.CreateWordListAsync(deletedListName);
            await db.DeleteWordListAsync(list.Id);

            var tombstones = await db.GetAllTombstonesAsync();
            Assert.Contains(tombstones, t => t.EntityType == "word_list" && t.EntityKey.Equals(deletedListName, StringComparison.OrdinalIgnoreCase));

            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 2. 模拟手机端发来包含该已删除词单的 payload
                var payload = new
                {
                    words = Array.Empty<object>(),
                    wordLists = new[]
                    {
                        new
                        {
                            name = deletedListName,
                            createdAt = DateTime.Now.AddDays(-1),
                            updatedAt = DateTime.Now.AddDays(-1)
                        }
                    },
                    reviewLogs = Array.Empty<object>(),
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                // 3. 验证该词单绝未在本地复活
                var allLists = await db.GetAllWordListsAsync();
                Assert.DoesNotContain(allLists, l => l.Name.Equals(deletedListName, StringComparison.OrdinalIgnoreCase));

                // 清理墓碑
                await db.RemoveTombstoneAsync("word_list", deletedListName);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task LanSyncService_ReviewLog_RatingOverride_Should_Update_InPlace_Without_Duplicate_Logs()
        {
            var db = new DatabaseService();
            string testWord = "同步改判词_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            await db.RemoveTombstoneAsync("word", testWord);

            DateTime reviewTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 10, 30, 15, DateTimeKind.Local);
            var localWord = new Models.Word
            {
                Text = testWord,
                CreatedAt = DateTime.Now.AddDays(-2),
                State = (int)Models.WordLearningState.Learning,
                Reps = 0,
                Stability = 0.0,
                Difficulty = 0.0
            };
            await db.AddWordAsync(localWord);
            var words = await db.GetAllWordsAsync();
            var insertedWord = words.Find(w => w.Text == testWord);
            Assert.NotNull(insertedWord);

            // 本地先记录一次 Rating=1 的打卡
            await db.UpdateWordFSRSAsync(insertedWord, 1, reviewTime);

            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 模拟手机端把同一条打卡记录（相同 reviewDate 秒级时间戳）改判为 Rating=3 (Good)
                var payload = new
                {
                    words = new[]
                    {
                        new
                        {
                            id = 777,
                            text = testWord,
                            state = (int)Models.WordLearningState.Learning,
                            reps = 1,
                            lapses = 0,
                            stability = 3.7145,
                            difficulty = 5.1618,
                            lastReviewDate = reviewTime,
                            stateUpdatedAt = DateTime.Now.AddSeconds(10)
                        }
                    },
                    wordLists = Array.Empty<object>(),
                    reviewLogs = new[]
                    {
                        new
                        {
                            wordId = 777,
                            wordText = testWord,
                            rating = 3,
                            reviewDate = reviewTime,
                            stabilityBefore = 0.0,
                            difficultyBefore = 0.0,
                            stabilityAfter = 3.7145,
                            difficultyAfter = 5.1618,
                            elapsedDays = 0,
                            scheduledDays = 4
                        }
                    },
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                // 验证：review_logs 应原地更新为 Rating=3，绝不产生两条重复日志！
                var allLogs = await db.GetAllReviewLogsAsync();
                var wordLogs = allLogs.FindAll(l => l.WordId == insertedWord.Id);
                Assert.Single(wordLogs);
                Assert.Equal(3, wordLogs[0].Rating);
                Assert.Equal(3.7145, wordLogs[0].StabilityAfter, 3);

                var updatedWord = (await db.GetAllWordsAsync()).Find(w => w.Id == insertedWord.Id);
                Assert.NotNull(updatedWord);
                Assert.Equal(1, updatedWord.Reps);
                Assert.Equal(0, updatedWord.Lapses);
                Assert.Equal(3.7145, updatedWord.Stability, 3);

                // 清理测试数据
                await db.DeleteWordsAsync(new[] { insertedWord.Id });
                await db.RemoveTombstoneAsync("word", testWord);
            }
            finally
            {
                service.Stop();
            }
        }

        [Fact]
        public async Task LanSyncService_SameDayCrossDeviceLogs_Should_DeduplicateToLatest_And_WriteOldTombstone()
        {
            var db = new DatabaseService();
            string testWord = "同日跨端去重词_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            await db.RemoveTombstoneAsync("word", testWord);

            DateTime morningTime = DateTime.Today.AddHours(9).AddMinutes(15).AddSeconds(10);
            DateTime afternoonTime = DateTime.Today.AddHours(16).AddMinutes(40).AddSeconds(25);
            string oldTombKey = $"{testWord.ToLowerInvariant()}_{morningTime:yyyyMMddHHmmss}";
            await db.RemoveTombstoneAsync("review_log", oldTombKey);

            var localWord = new Models.Word
            {
                Text = testWord,
                CreatedAt = DateTime.Today.AddDays(-3),
                State = (int)Models.WordLearningState.New,
                Reps = 0
            };
            await db.AddWordAsync(localWord);
            var insertedWord = (await db.GetAllWordsAsync()).Find(w => w.Text == testWord)!;

            // 本地早上 09:15:10 打卡 Again(1)
            await db.UpdateWordFSRSAsync(insertedWord, 1, morningTime);

            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 手机端同一自然日下午 16:40:25 离线打卡了同一个词 Good(3)
                var payload = new
                {
                    words = new[]
                    {
                        new
                        {
                            id = 888,
                            text = testWord,
                            state = (int)Models.WordLearningState.Learning,
                            reps = 1,
                            lapses = 0,
                            stability = 3.7145,
                            difficulty = 5.1618,
                            lastReviewDate = afternoonTime,
                            stateUpdatedAt = afternoonTime
                        }
                    },
                    wordLists = Array.Empty<object>(),
                    reviewLogs = new[]
                    {
                        new
                        {
                            wordId = 888,
                            rating = 3,
                            reviewDate = afternoonTime,
                            stabilityBefore = 0.0,
                            difficultyBefore = 0.0,
                            stabilityAfter = 3.7145,
                            difficultyAfter = 5.1618,
                            elapsedDays = 0,
                            scheduledDays = 4
                        }
                    },
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                // 验证 1：同一自然日跨设备多条 ReviewLog 自动归并为最新一条（下午 16:40:25）
                var wordLogs = (await db.GetAllReviewLogsAsync()).FindAll(l => l.WordId == insertedWord.Id);
                Assert.Single(wordLogs);
                Assert.Equal(3, wordLogs[0].Rating);
                Assert.Equal(afternoonTime.ToString("yyyyMMddHHmmss"), wordLogs[0].ReviewDate.ToString("yyyyMMddHHmmss"));

                // 验证 2：旧时间戳（早上 09:15:10）已自动生成 review_log 墓碑防复活
                var tombstones = await db.GetAllTombstonesAsync();
                Assert.Contains(tombstones, t => t.EntityType == "review_log" && t.EntityKey == oldTombKey);

                // 验证 3：单词 FSRS 状态与归并后的唯一日志保持一致（Reps=1，绝不虚增为 2）
                var mergedWord = (await db.GetAllWordsAsync()).Find(w => w.Id == insertedWord.Id)!;
                Assert.Equal(1, mergedWord.Reps);
                Assert.Equal(0, mergedWord.Lapses);
                Assert.Equal(3.7145, mergedWord.Stability, 3);
            }
            finally
            {
                await db.DeleteWordsAsync(new[] { insertedWord.Id });
                await db.RemoveTombstoneAsync("word", testWord);
                await db.RemoveTombstoneAsync("review_log", oldTombKey);
                service.Stop();
            }
        }

        [Fact]
        public async Task LanSyncService_OfflineCrossDayReviews_Should_TriggerTimelineReplayCalibration_And_MasteredPreservesStability()
        {
            var db = new DatabaseService();
            string replayWordText = "跨日重演校准词_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string masteredWordText = "掌握免篡改词_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            await db.RemoveTombstoneAsync("word", replayWordText);
            await db.RemoveTombstoneAsync("word", masteredWordText);

            DateTime createdDate = DateTime.Today.AddDays(-6);
            DateTime dayMinus4 = DateTime.Today.AddDays(-4).AddHours(10);
            DateTime dayMinus1 = DateTime.Today.AddDays(-1).AddHours(15);

            var localReplayWord = new Models.Word
            {
                Text = replayWordText,
                CreatedAt = createdDate,
                State = (int)Models.WordLearningState.New,
                Reps = 0
            };
            var localMasteredCandidate = new Models.Word
            {
                Text = masteredWordText,
                CreatedAt = createdDate,
                State = (int)Models.WordLearningState.New,
                Reps = 0
            };
            await db.AddWordAsync(localReplayWord);
            await db.AddWordAsync(localMasteredCandidate);

            var allInitWords = await db.GetAllWordsAsync();
            var insertedReplay = allInitWords.Find(w => w.Text == replayWordText)!;
            var insertedMastered = allInitWords.Find(w => w.Text == masteredWordText)!;

            // 本地端在 4 天前完成了第 1 次复习 Good(3) -> S = 3.7145, Reps = 1
            await db.UpdateWordFSRSAsync(insertedReplay, 3, dayMinus4);
            await db.UpdateWordFSRSAsync(insertedMastered, 3, dayMinus4);

            var service = LanSyncService.Instance;
            await service.StartAsync();

            try
            {
                using var http = new HttpClient();
                string pushUrl = $"http://127.0.0.1:{service.Port}/api/sync/push";

                // 模拟手机端：
                // 1) replayWord 在离线状态下于昨天(dayMinus1)独立打卡了 Good(3)（手机端当时以为是首次打卡 Reps=1, StabilityBefore=0）
                // 2) masteredWord 在手机端被手动设为 Mastered(3)（且携带旧版伪造的 stability=25.0, reps=1, nextReviewDate=明天）
                var payload = new
                {
                    words = new object[]
                    {
                        new
                        {
                            id = 901,
                            text = replayWordText,
                            state = (int)Models.WordLearningState.Learning,
                            reps = 1,
                            lapses = 0,
                            stability = 3.7145,
                            difficulty = 5.1618,
                            lastReviewDate = dayMinus1,
                            stateUpdatedAt = dayMinus1
                        },
                        new
                        {
                            id = 902,
                            text = masteredWordText,
                            state = (int)Models.WordLearningState.Mastered,
                            reps = 1,
                            lapses = 0,
                            stability = 25.0, // 模拟外部伪造的 25.0
                            difficulty = 5.1618,
                            lastReviewDate = dayMinus4,
                            nextReviewDate = DateTime.Today.AddDays(10),
                            stateUpdatedAt = DateTime.Now
                        }
                    },
                    wordLists = Array.Empty<object>(),
                    reviewLogs = new[]
                    {
                        new
                        {
                            wordId = 901,
                            rating = 3,
                            reviewDate = dayMinus1,
                            stabilityBefore = 0.0, // 离线分裂时的旧快照
                            difficultyBefore = 0.0,
                            stabilityAfter = 3.7145,
                            difficultyAfter = 5.1618,
                            elapsedDays = 0,
                            scheduledDays = 4
                        }
                    },
                    tombstones = Array.Empty<object>()
                };

                var pushReq = new HttpRequestMessage(HttpMethod.Post, pushUrl);
                pushReq.Headers.Add("X-Sync-Pin", service.CurrentPin);
                pushReq.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var pushRes = await http.SendAsync(pushReq);
                Assert.True(pushRes.IsSuccessStatusCode);

                var afterWords = await db.GetAllWordsAsync();
                var syncedReplay = afterWords.Find(w => w.Id == insertedReplay.Id)!;
                var syncedMastered = afterWords.Find(w => w.Id == insertedMastered.Id)!;

                // 验证跨日离线打卡合并后自动触发 FSRS 时间轴重放校准：
                // 第 1 次 (dayMinus4, Good) -> 第 2 次 (dayMinus1, 间隔 3 个自然日, Good)
                Assert.Equal(2, syncedReplay.Reps);
                Assert.Equal((int)Models.WordLearningState.Review, syncedReplay.State);
                Assert.True(syncedReplay.Stability > 3.7145, $"重放后第2次复习稳定性应高于首次: {syncedReplay.Stability}");

                var replayLogs = (await db.GetAllReviewLogsAsync())
                    .FindAll(l => l.WordId == insertedReplay.Id);
                Assert.Equal(2, replayLogs.Count);
                Assert.Equal(3.7145, replayLogs[1].StabilityBefore, 3);
                Assert.Equal(3, replayLogs[1].ElapsedDays);
                Assert.Equal(syncedReplay.Stability, replayLogs[1].StabilityAfter, 4);

                // 验证 Mastered 状态同步时 NextReviewDate 为 null 且绝不被伪造的 25.0 覆盖本地真实 Stability(3.7145)
                Assert.Equal((int)Models.WordLearningState.Mastered, syncedMastered.State);
                Assert.Null(syncedMastered.NextReviewDate);
                Assert.Equal(3.7145, syncedMastered.Stability, 3);
                Assert.NotEqual(25.0, syncedMastered.Stability);
            }
            finally
            {
                await db.DeleteWordsAsync(new[] { insertedReplay.Id, insertedMastered.Id });
                await db.RemoveTombstoneAsync("word", replayWordText);
                await db.RemoveTombstoneAsync("word", masteredWordText);
                service.Stop();
            }
        }
    }
}
