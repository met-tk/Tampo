using System;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class FsrsEngineTests
    {
        [Fact]
        public void FirstReview_Good_InitializesStabilityAndDifficultyCorrectly()
        {
            var word = new Word
            {
                Text = "猫",
                Reps = 0,
                Stability = 0.0,
                Difficulty = 0.0
            };

            var now = DateTime.Now;
            var (newS, newD, nextReview) = FsrsEngine.Review(word, 3, now); // 3 = Good

            Assert.True(newS > 0.0, "初始稳定性应大于 0");
            Assert.True(newD >= 1.0 && newD <= 10.0, "初始难度应在 [1.0, 10.0] 之间");
            Assert.True(nextReview > now, "下次复习时间应晚于当前时间");
        }

        [Fact]
        public void FirstReview_Again_InitializesWithLowerStability()
        {
            var wordGood = new Word { Text = "猫", Reps = 0 };
            var wordAgain = new Word { Text = "犬", Reps = 0 };

            var now = DateTime.Now;
            var (sGood, _, _) = FsrsEngine.Review(wordGood, 3, now);
            var (sAgain, _, _) = FsrsEngine.Review(wordAgain, 1, now);

            Assert.True(sGood > sAgain, "记得(Good)的初始稳定性必须高于遗忘(Again)");
        }

        [Fact]
        public void SubsequentReview_Good_IncreasesStability()
        {
            var now = DateTime.Now;
            var word = new Word
            {
                Text = "桜",
                Reps = 1,
                Stability = 3.17,
                Difficulty = 5.0,
                CreatedAt = now.AddDays(-3),
                LastReviewDate = now.AddDays(-3)
            };

            var (newS, _, _) = FsrsEngine.Review(word, 3, now);

            Assert.True(newS > word.Stability, "成功复习后稳定性必须增长");
        }

        [Fact]
        public void CalculateRetrievability_DecaysOverTime()
        {
            double stability = 10.0;
            var start = DateTime.Now;

            double rDay0 = FsrsEngine.CalculateRetrievability(stability, start, start);
            double rDay5 = FsrsEngine.CalculateRetrievability(stability, start, start.AddDays(5));
            double rDay20 = FsrsEngine.CalculateRetrievability(stability, start, start.AddDays(20));

            Assert.Equal(1.0, rDay0, 3);
            Assert.True(rDay5 < rDay0, "5天后的留存率应低于第0天");
            Assert.True(rDay20 < rDay5, "20天后的留存率应更低");
            Assert.True(rDay20 > 0.0, "留存率应始终保持非负");
        }

        [Fact]
        public void NextIntervalDays_IncreasesWithHigherStability()
        {
            int intervalShort = FsrsEngine.NextIntervalDays(2.0);
            int intervalLong = FsrsEngine.NextIntervalDays(15.0);

            Assert.True(intervalLong > intervalShort, "稳定性越高，推荐复习间隔天数越长");
        }

        [Fact]
        public void FsrsAlgorithm_DecoupledFromMasteredState_AlwaysYieldsLearningOrReview()
        {
            // 验证即使稳定性达到极大值（如 100 天），算法计算的下次状态只应为 Review，而非自动跃升 Mastered
            var now = DateTime.Now;
            var word = new Word
            {
                Text = "漢字",
                Reps = 10,
                Stability = 80.0,
                Difficulty = 3.0,
                CreatedAt = now.AddDays(-120),
                LastReviewDate = now.AddDays(-80),
                State = (int)WordLearningState.Review
            };

            var (newS, newD, nextReview) = FsrsEngine.Review(word, 3, now);

            Assert.True(newS > 80.0);
            Assert.True(nextReview > now.AddDays(60));
            // 确认 WordLearningState.Mastered 不作为算法默认状态触发
            Assert.Equal(3, (int)WordLearningState.Mastered);
        }

        [Fact]
        public void StateTransition_FirstRemembered_IsLearning_NotReviewOrMastered()
        {
            var word = new Word
            {
                Text = "新しい",
                Reps = 0,
                State = (int)WordLearningState.New
            };

            int nextState;
            int rating = 3; // 记得
            if (rating == 1)
            {
                nextState = (int)WordLearningState.Learning;
            }
            else
            {
                nextState = word.Reps == 0 ? (int)WordLearningState.Learning : (int)WordLearningState.Review;
            }

            Assert.Equal((int)WordLearningState.Learning, nextState);
        }

        [Fact]
        public void StateTransition_ConsolidatedReview_ElevatesToReview()
        {
            var word = new Word
            {
                Text = "復習",
                Reps = 1,
                State = (int)WordLearningState.Learning
            };

            int nextState;
            int rating = 3; // 记得
            if (rating == 1)
            {
                nextState = (int)WordLearningState.Learning;
            }
            else
            {
                nextState = word.Reps == 0 ? (int)WordLearningState.Learning : (int)WordLearningState.Review;
            }

            Assert.Equal((int)WordLearningState.Review, nextState);
        }

        [Fact]
        public void FsrsState_FourStates_ClassifiedCorrectly()
        {
            var wordNew = new Word { Text = "新词", Reps = 0, State = (int)WordLearningState.New };
            var wordLearning = new Word { Text = "初学", Reps = 1, Lapses = 0, State = (int)WordLearningState.Learning };
            var wordReview = new Word { Text = "复习", Reps = 3, Lapses = 0, State = (int)WordLearningState.Review };
            var wordRelearning = new Word { Text = "重学", Reps = 2, Lapses = 1, State = (int)WordLearningState.Learning };

            Assert.Equal(FsrsScheduleState.New, wordNew.FsrsState);
            Assert.Equal(FsrsScheduleState.Learning, wordLearning.FsrsState);
            Assert.Equal(FsrsScheduleState.Review, wordReview.FsrsState);
            Assert.Equal(FsrsScheduleState.Relearning, wordRelearning.FsrsState);
        }

        [Fact]
        public void SubsequentReview_NextDifficulty_RevertsTowardsD0Good()
        {
            var now = DateTime.Now;
            var hardWord = new Word
            {
                Text = "難解",
                Reps = 3,
                Stability = 5.0,
                Difficulty = 8.5,
                CreatedAt = now.AddDays(-10),
                LastReviewDate = now.AddDays(-5)
            };

            var (_, nextD, _) = FsrsEngine.Review(hardWord, 3, now); // Grade = 3 (Good)

            // D0(3) 约为 5.28，在 Grade=3 且初始难度为 8.5 时，新难度应向 5.28 回归（即必然小于 8.5）
            Assert.True(nextD < 8.5, $"难度应向 D0(3) 均值回归，实际为 {nextD}");
            Assert.True(nextD >= 1.0 && nextD <= 10.0);
        }

        [Fact]
        public async System.Threading.Tasks.Task RevertTodayReview_WithMultipleReps_RestoresPreviousStateAndDueNow()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_revert_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "林檎" });
                var words = await db.GetAllWordsAsync();
                var word = words[0];

                // 第一次复习：昨天记得
                DateTime yesterday = DateTime.Today.AddDays(-1).AddHours(10);
                await db.UpdateWordFSRSAsync(word, 3, yesterday);

                words = await db.GetAllWordsAsync();
                word = words[0];
                double firstStability = word.Stability;
                Assert.Equal(1, word.Reps);

                // 第二次复习：今天手误点击了遗忘 (rating = 1)
                DateTime today = DateTime.Today.AddHours(14);
                await db.UpdateWordFSRSAsync(word, 1, today);

                words = await db.GetAllWordsAsync();
                word = words[0];
                Assert.Equal(2, word.Reps);
                Assert.Equal(1, word.Lapses);

                var todayLogs = await db.GetTodayReviewItemsAsync();
                Assert.Single(todayLogs);

                // 撤销今天的复习
                await db.RevertTodayReviewAsync(todayLogs[0].LogId);

                words = await db.GetAllWordsAsync();
                word = words[0];

                // 验证 Reps 回退为 1，Lapses 回退为 0
                Assert.Equal(1, word.Reps);
                Assert.Equal(0, word.Lapses);
                // 验证 Stability 回滚为第一次复习后的稳定性
                Assert.Equal(firstStability, word.Stability);
                // 验证 LastReviewDate 回退为昨天的打卡时间
                Assert.NotNull(word.LastReviewDate);
                Assert.Equal(yesterday.Date, word.LastReviewDate.Value.Date);
                // 验证 NextReviewDate 变为今日当前时间，使得该词今天立即可重新复习！
                Assert.NotNull(word.NextReviewDate);
                Assert.True(word.NextReviewDate.Value <= DateTime.Now.AddSeconds(5));
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task GetStudyQueue_IncludesWordDueLaterToday_AndExcludesAlreadyReviewedToday()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_queue_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "朝", "昼", "夜" });
                var words = await db.GetAllWordsAsync();
                var wordNight = words.First(w => w.Text == "夜");
                var wordTomorrow = words.First(w => w.Text == "昼");
                var wordDoneToday = words.First(w => w.Text == "朝");

                // 词 1: NextReviewDate 设在今晚 23:00（即便当前是白天，按自然日也应纳入今日复习队列）
                wordNight.Reps = 1;
                wordNight.State = (int)WordLearningState.Learning;
                wordNight.LastReviewDate = DateTime.Today.AddDays(-1);
                wordNight.NextReviewDate = DateTime.Today.AddHours(23);
                await db.UpdateWordAsync(wordNight);

                // 词 2: NextReviewDate 设在明天中午（不应出现在今日队列）
                wordTomorrow.Reps = 1;
                wordTomorrow.State = (int)WordLearningState.Learning;
                wordTomorrow.LastReviewDate = DateTime.Today.AddDays(-1);
                wordTomorrow.NextReviewDate = DateTime.Today.AddDays(1).AddHours(12);
                await db.UpdateWordAsync(wordTomorrow);

                // 词 3: 今天已经复习过（严格排除，杜绝一日重复刷词）
                wordDoneToday.Reps = 1;
                wordDoneToday.State = (int)WordLearningState.Learning;
                wordDoneToday.LastReviewDate = DateTime.Now;
                wordDoneToday.NextReviewDate = DateTime.Now.AddHours(-1);
                await db.UpdateWordAsync(wordDoneToday);

                var queue = await db.GetStudyQueueAsync();

                Assert.Contains(queue, w => w.Text == "夜");
                Assert.DoesNotContain(queue, w => w.Text == "昼");
                Assert.DoesNotContain(queue, w => w.Text == "朝");
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public void Fsrs45_OfficialFormula_LinearInitDifficultyAndForgetStabilityUpperBound()
        {
            Assert.Equal(17, FsrsEngine.W.Length);

            var now = DateTime.Now;
            var wordGood = new Word { Text = "公式G3", Reps = 0 };
            var (s3, d3, _) = FsrsEngine.Review(wordGood, 3, now);
            Assert.Equal(3.7145, s3, 4);
            Assert.Equal(5.1618, d3, 4);

            var wordAgain = new Word { Text = "公式G1", Reps = 0 };
            var (s1, d1, _) = FsrsEngine.Review(wordAgain, 1, now);
            Assert.Equal(0.4872, s1, 4);
            // D0(1) = 5.1618 - 1.2298 * (1 - 3) = 7.6214
            Assert.Equal(7.6214, d1, 4);

            // 验证遗忘后稳定性上限 S'_f <= S，且间隔固定为 1 天
            var reviewWord = new Word
            {
                Text = "遗忘上限",
                Reps = 2,
                Stability = 0.5,
                Difficulty = 2.0,
                CreatedAt = now.AddDays(-1),
                LastReviewDate = now.AddDays(-1)
            };
            var (sForget, _, nextDue) = FsrsEngine.Review(reviewWord, 1, now);
            Assert.True(sForget <= reviewWord.Stability, $"遗忘后稳定性 {sForget} 不得超过原稳定性 {reviewWord.Stability}");
            Assert.True(sForget >= 0.1);
            Assert.Equal(1, (int)Math.Round((nextDue - now).TotalDays));
        }

        [Fact]
        public async System.Threading.Tasks.Task ChangeTodayReviewRating_RecalculatesFsrsAndUpdatesReviewLog_AndNoOpWhenSame()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_change_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "改判" });
                var words = await db.GetAllWordsAsync();
                var word = words[0];

                DateTime today = DateTime.Today.AddHours(10);
                // 首次打卡：误点“不记得” (rating = 1)
                await db.UpdateWordFSRSAsync(word, 1, today);

                words = await db.GetAllWordsAsync();
                word = words[0];
                Assert.Equal(1, word.Reps);
                Assert.Equal(1, word.Lapses);
                Assert.Equal(0.4872, word.Stability, 3);

                var todayItems = await db.GetTodayReviewItemsAsync();
                Assert.Single(todayItems);
                int logId = todayItems[0].LogId;

                // 重复改判相同 Rating (1 -> 1) 应短路无副作用
                await db.ChangeTodayReviewRatingAsync(logId, 1);
                words = await db.GetAllWordsAsync();
                Assert.Equal(1, words[0].Lapses);

                // 改判为“记得” (1 -> 3)：应重新按首次复习 Good 计算 S=3.7145, D=5.1618, Lapses=0
                await db.ChangeTodayReviewRatingAsync(logId, 3);
                words = await db.GetAllWordsAsync();
                word = words[0];
                Assert.Equal(1, word.Reps);
                Assert.Equal(0, word.Lapses);
                Assert.Equal(3.7145, word.Stability, 3);
                Assert.Equal(5.1618, word.Difficulty, 3);

                todayItems = await db.GetTodayReviewItemsAsync();
                Assert.Equal(3, todayItems[0].Rating);
                var allLogs = await db.GetAllReviewLogsAsync();
                Assert.Single(allLogs);
                Assert.Equal(3.7145, allLogs[0].StabilityAfter, 3);
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task UpdateWordsState_ResetToNew_ClearsLastReviewDateAndWritesLogTombstones()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_relearn_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "リセット" });
                var word = (await db.GetAllWordsAsync())[0];

                await db.UpdateWordFSRSAsync(word, 1, DateTime.Now);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal(1, word.Lapses);
                Assert.NotNull(word.LastReviewDate);

                // 重置为 New（重新学习）
                await db.UpdateWordsStateAsync(new[] { word.Id }, WordLearningState.New);

                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.New, word.State);
                Assert.Equal(0, word.Reps);
                Assert.Equal(0, word.Lapses);
                Assert.Equal(0, word.Stability);
                Assert.Null(word.LastReviewDate);
                Assert.Empty(await db.GetAllReviewLogsAsync());

                var tombstones = await db.GetAllTombstonesAsync();
                Assert.Contains(tombstones, t => t.EntityType == "review_log");

                // 再次出现在今日待学新词队列中
                var queue = await db.GetStudyQueueAsync();
                Assert.Contains(queue, w => w.Id == word.Id);
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public void GetCalendarElapsedDays_CrossMidnightAndSameDay_CalculatesExactNaturalDays()
        {
            // 跨自然日：昨天 23:50 到今天 00:10 尽管仅差 20 分钟，按自然日必须计为 1.0 天
            var yesterdayLate = new DateTime(2026, 10, 5, 23, 50, 0, DateTimeKind.Local);
            var todayEarly = new DateTime(2026, 10, 6, 0, 10, 0, DateTimeKind.Local);
            Assert.Equal(1.0, FsrsEngine.GetCalendarElapsedDays(yesterdayLate, todayEarly));

            // 同自然日：今天 08:00 到今天 22:00 间隔 14 小时，按自然日必须计为 0.0 天
            var todayMorning = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Local);
            var todayNight = new DateTime(2026, 10, 6, 22, 0, 0, DateTimeKind.Local);
            Assert.Equal(0.0, FsrsEngine.GetCalendarElapsedDays(todayMorning, todayNight));

            // 异常时钟回拨防御：未来时间不产生负数天数
            Assert.Equal(0.0, FsrsEngine.GetCalendarElapsedDays(todayNight, yesterdayLate));

            // UTC 自动转本地自然日对齐验证
            Assert.Equal(1.0, FsrsEngine.GetCalendarElapsedDays(yesterdayLate.ToUniversalTime(), todayEarly.ToUniversalTime()));
        }

        [Fact]
        public void CalculateRetrievability_UsesCalendarDay_AndHandlesInvalidStability()
        {
            double stability = 10.0;
            var todayMorning = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Local);
            var todayNight = new DateTime(2026, 10, 6, 23, 30, 0, DateTimeKind.Local);
            var yesterdayLate = new DateTime(2026, 10, 5, 23, 55, 0, DateTimeKind.Local);
            var todayEarly = new DateTime(2026, 10, 6, 0, 5, 0, DateTimeKind.Local);

            // 同自然日内留存率恒为 1.0
            Assert.Equal(1.0, FsrsEngine.CalculateRetrievability(stability, todayMorning, todayNight), 6);

            // 跨自然日按整 1.0 天幂律衰减：R(1, 10) = (1 + (19/81)*(1/10))^(-0.5)
            double expectedDay1 = Math.Pow(1.0 + (19.0 / 81.0) * (1.0 / stability), -0.5);
            Assert.Equal(expectedDay1, FsrsEngine.CalculateRetrievability(stability, yesterdayLate, todayEarly), 6);

            // 脏数据防御
            Assert.Equal(0.0, FsrsEngine.CalculateRetrievability(0.0, yesterdayLate, todayEarly));
            Assert.Equal(0.0, FsrsEngine.CalculateRetrievability(-5.0, yesterdayLate, todayEarly));
            Assert.Equal(0.0, FsrsEngine.CalculateRetrievability(double.NaN, yesterdayLate, todayEarly));
            Assert.Equal(0.0, FsrsEngine.CalculateRetrievability(double.PositiveInfinity, yesterdayLate, todayEarly));
        }

        [Fact]
        public void BinaryRating_DifficultyBalance_GoodDecreasesAndAgainIncreases()
        {
            var day0 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Local);
            var word = new Word { Text = "均衡", CreatedAt = day0, Reps = 0 };

            // 初次 Good(3) -> D0(3) = 5.1618
            var (s1, d1, _) = FsrsEngine.Review(word, 3, day0);
            Assert.Equal(5.1618, d1, 4);

            word.Reps = 1;
            word.Stability = s1;
            word.Difficulty = d1;
            word.LastReviewDate = day0;

            // 连续多次 Good(3) 时难度 D 必须合理下降（绝不因 g-3==0 卡死在 5.1618）
            var day4 = day0.AddDays(4);
            var (s2, d2, _) = FsrsEngine.Review(word, 3, day4);
            Assert.True(d2 < d1, $"连续 Good(3) 后难度应下降: d2={d2} < d1={d1}");

            word.Reps = 2;
            word.Stability = s2;
            word.Difficulty = d2;
            word.LastReviewDate = day4;

            var day15 = day4.AddDays(11);
            var (s3, d3, _) = FsrsEngine.Review(word, 3, day15);
            Assert.True(d3 < d2, $"再次 Good(3) 后难度应继续下降: d3={d3} < d2={d2}");

            // 评 Again(1) 时难度显著上升
            word.Reps = 3;
            word.Stability = s3;
            word.Difficulty = d3;
            word.LastReviewDate = day15;

            var (_, dAgain, _) = FsrsEngine.Review(word, 1, day15.AddDays(20));
            Assert.True(dAgain > d3, $"评 Again(1) 后难度应上升: dAgain={dAgain} > d3={d3}");
        }

        [Fact]
        public void SameCalendarDay_ReviewGood_ProtectsStabilityFromDecreasing()
        {
            var morning = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Local);
            var evening = new DateTime(2026, 10, 6, 21, 0, 0, DateTimeKind.Local);

            var word = new Word
            {
                Text = "同日保护",
                Reps = 2,
                Stability = 8.5,
                Difficulty = 5.0,
                CreatedAt = morning.AddDays(-5),
                LastReviewDate = morning
            };

            var (newS, _, _) = FsrsEngine.Review(word, 3, evening);
            Assert.True(newS >= word.Stability, $"同自然日内评 Good(3) 稳定性不得倒退: newS={newS}, oldS={word.Stability}");
        }

        [Fact]
        public void Review_And_Helpers_DefendAgainstDirtyData_NaN_Infinity_Negative()
        {
            var now = DateTime.Now;
            var dirtyWord = new Word
            {
                Text = "脏数据词",
                Reps = 5,
                Stability = double.NaN,
                Difficulty = double.PositiveInfinity,
                LastReviewDate = now.AddDays(-3)
            };

            var (s1, d1, next1) = FsrsEngine.Review(dirtyWord, 99, now); // 评级越界 + NaN/Inf
            Assert.False(double.IsNaN(s1) || double.IsInfinity(s1));
            Assert.False(double.IsNaN(d1) || double.IsInfinity(d1));
            Assert.InRange(s1, 0.1, 36500.0);
            Assert.InRange(d1, 1.0, 10.0);
            Assert.True(next1 > now);

            var negativeWord = new Word
            {
                Text = "负数词",
                Reps = 3,
                Stability = -100.0,
                Difficulty = -5.0,
                LastReviewDate = now.AddDays(-2)
            };
            var (s2, d2, _) = FsrsEngine.Review(negativeWord, 0, now);
            Assert.InRange(s2, 0.1, 36500.0);
            Assert.InRange(d2, 1.0, 10.0);

            Assert.Equal(1, FsrsEngine.NextIntervalDays(double.NaN));
            Assert.Equal(1, FsrsEngine.NextIntervalDays(double.PositiveInfinity));
            Assert.Equal(1, FsrsEngine.NextIntervalDays(-5.0));

            Assert.InRange(FsrsEngine.NextDifficulty(double.NaN, 3), 1.0, 10.0);
            Assert.InRange(FsrsEngine.NextRecallStability(double.NaN, double.PositiveInfinity, 0.9, 3), 0.1, 36500.0);
            Assert.InRange(FsrsEngine.NextForgetStability(double.NegativeInfinity, -1.0, 0.5), 0.1, 36500.0);
        }

        [Fact]
        public void Word_DomainAxioms_MasteredAndNewRetrievability_AndDayLevelIntervalText()
        {
            var masteredWord = new Word
            {
                Text = "掌握词",
                State = (int)WordLearningState.Mastered,
                Stability = 0.0,
                Reps = 0,
                LastReviewDate = DateTime.Today.AddDays(-100)
            };
            Assert.Equal(1.0, masteredWord.CurrentRetrievability);

            var newWord = new Word
            {
                Text = "未学词",
                State = (int)WordLearningState.New,
                Stability = 10.0,
                Reps = 0
            };
            Assert.Equal(0.0, newWord.CurrentRetrievability);

            var dueTodayWord = new Word
            {
                Text = "今日到期",
                State = (int)WordLearningState.Learning,
                Reps = 1,
                Stability = 1.0,
                NextReviewDate = DateTime.Today.AddHours(23)
            };
            var due5DaysWord = new Word
            {
                Text = "五天后",
                State = (int)WordLearningState.Review,
                Reps = 2,
                Stability = 5.0,
                NextReviewDate = DateTime.Today.AddDays(5)
            };

            foreach (var w in new[] { masteredWord, newWord, dueTodayWord, due5DaysWord })
            {
                string intervalText = w.NextReviewIntervalText;
                Assert.DoesNotContain("分钟", intervalText);
                Assert.DoesNotContain("小时", intervalText);
                Assert.DoesNotContain("min", intervalText, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hour", intervalText, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task UpdateWordsState_MasteredPreservesFsrsParamsAndClearsNextReview_NonMasteredResetsToNew()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_state_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "手動状態" });
                var word = (await db.GetAllWordsAsync())[0];

                var revTime = DateTime.Today.AddDays(-2).AddHours(10);
                await db.UpdateWordFSRSAsync(word, 3, revTime);
                word = (await db.GetAllWordsAsync())[0];
                double originalStability = word.Stability;
                Assert.Equal(3.7145, originalStability, 4);
                Assert.NotNull(word.NextReviewDate);

                // 1. 手动设为 Mastered：仅设置 State=Mastered 且 NextReviewDate=null，严禁篡改 Stability=25.0
                await db.UpdateWordsStateAsync(new[] { word.Id }, WordLearningState.Mastered);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.Mastered, word.State);
                Assert.Null(word.NextReviewDate);
                Assert.Equal(originalStability, word.Stability, 4);
                Assert.NotEqual(25.0, word.Stability);
                Assert.Equal(1, word.Reps);

                // 2. 传入非 Mastered（如 Learning）时一律重置为 New(0) 并清空复习日志
                await db.UpdateWordsStateAsync(new[] { word.Id }, WordLearningState.Learning);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.New, word.State);
                Assert.Equal(0, word.Reps);
                Assert.Equal(0.0, word.Stability);
                Assert.Null(word.LastReviewDate);
                Assert.Null(word.NextReviewDate);
                Assert.Empty(await db.GetAllReviewLogsAsync());
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task SameDayReview_IsIdempotent_AndMultiDayReplayOnChangeAndRevertIsExact()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_idempotent_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "冪等重放" });
                var word = (await db.GetAllWordsAsync())[0];

                // 第 1 天：Good(3)
                var day1 = DateTime.Today.AddDays(-5).AddHours(9);
                await db.UpdateWordFSRSAsync(word, 3, day1);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal(1, word.Reps);
                double day1Stability = word.Stability;

                // 今天上午：首次打卡 Again(1)
                var todayMorning = DateTime.Today.AddHours(9);
                await db.UpdateWordFSRSAsync(word, 1, todayMorning);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal(2, word.Reps);
                Assert.Equal(1, word.Lapses);

                // 今天下午：同自然日再次调用 UpdateWordFSRSAsync 评为 Good(3) -> 必须原地幂等重算，不新增日志、不虚增 Reps
                var todayAfternoon = DateTime.Today.AddHours(15);
                await db.UpdateWordFSRSAsync(word, 3, todayAfternoon);
                word = (await db.GetAllWordsAsync())[0];
                var logs = await db.GetWordReviewLogsAsync(word.Id);
                Assert.Equal(2, word.Reps);
                Assert.Equal(0, word.Lapses);
                Assert.Equal(2, logs.Count);

                // 今天通过 ChangeTodayReviewRatingAsync 改判为 Again(1) -> 基于日志重放
                var todayItems = await db.GetTodayReviewItemsAsync();
                Assert.Single(todayItems);
                await db.ChangeTodayReviewRatingAsync(todayItems[0].LogId, 1);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal(2, word.Reps);
                Assert.Equal(1, word.Lapses);

                // 撤销今天打卡 -> 精确回滚至第 1 天的 FSRS 状态
                await db.RevertTodayReviewAsync(todayItems[0].LogId);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal(1, word.Reps);
                Assert.Equal(0, word.Lapses);
                Assert.Equal(day1Stability, word.Stability, 4);
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }

        [Fact]
        public void NextReviewDate_IsStrictlyNormalizedToMidnight()
        {
            var word = new Word { Text = "零点归一化", Reps = 1, Stability = 5.0, Difficulty = 4.0 };
            DateTime lateNight = new DateTime(2026, 10, 6, 23, 59, 58, 888);
            var (s, d, nextReview) = FsrsEngine.Review(word, 3, lateNight);

            Assert.Equal(0, nextReview.Hour);
            Assert.Equal(0, nextReview.Minute);
            Assert.Equal(0, nextReview.Second);
            Assert.Equal(0, nextReview.Millisecond);
            Assert.True(nextReview > lateNight);
        }

        [Fact]
        public void Word_FsrsState_FirstReviewLapse_IsLearning_NotRelearning()
        {
            // 首轮初学阶段（Reps == 1, Lapses == 1），未曾进入过 Review 期，严格归入 Learning
            var firstLapseWord = new Word
            {
                Text = "初学遗忘",
                State = (int)WordLearningState.Learning,
                Reps = 1,
                Lapses = 1
            };
            Assert.Equal(FsrsScheduleState.Learning, firstLapseWord.FsrsState);

            // 经历过 2 次以上复习且发生 Lapses，跌落回短周期阶梯，判定为 Relearning
            var relearnWord = new Word
            {
                Text = "重学遗忘",
                State = (int)WordLearningState.Learning,
                Reps = 3,
                Lapses = 1
            };
            Assert.Equal(FsrsScheduleState.Relearning, relearnWord.FsrsState);
        }

        [Fact]
        public void Word_IsDue_MatchesAndroidDomainContract()
        {
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);

            // 1. 已掌握 (Mastered) 恒不到期
            var masteredWord = new Word
            {
                Text = "掌握词",
                State = (int)WordLearningState.Mastered,
                Reps = 5,
                NextReviewDate = yesterday
            };
            Assert.False(masteredWord.IsDue);

            // 2. 今日已复习过的词，严格不到期
            var reviewedToday = new Word
            {
                Text = "今日已学",
                State = (int)WordLearningState.Review,
                Reps = 2,
                LastReviewDate = DateTime.Now,
                NextReviewDate = today
            };
            Assert.False(reviewedToday.IsDue);

            // 3. 从未学习过的新词或到期词，属于到期
            var newWord = new Word { Text = "全新词", State = (int)WordLearningState.New, Reps = 0 };
            Assert.True(newWord.IsDue);

            var dueWord = new Word
            {
                Text = "今日到期",
                State = (int)WordLearningState.Review,
                Reps = 2,
                LastReviewDate = yesterday,
                NextReviewDate = today
            };
            Assert.True(dueWord.IsDue);
        }

        [Fact]
        public async System.Threading.Tasks.Task ChangeTodayReviewRating_WhenMastered_KeepsMasteredAndNullNextReviewDate()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_mastered_change_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();

                await db.AddWordsAsync(new[] { "桜" });
                var word = (await db.GetAllWordsAsync())[0];

                // 用户将单词手动标记为已掌握
                await db.UpdateWordsStateAsync(new[] { word.Id }, WordLearningState.Mastered);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.Mastered, word.State);
                Assert.Null(word.NextReviewDate);

                // 在今日打卡评定为 3 (记得)
                await db.UpdateWordFSRSAsync(word, 3, DateTime.Now);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.Mastered, word.State);
                Assert.Null(word.NextReviewDate);

                // 在今日记录中更正评分为 1 (遗忘)
                var todayLogs = await db.GetTodayReviewItemsAsync();
                Assert.Single(todayLogs);
                await db.ChangeTodayReviewRatingAsync(todayLogs[0].LogId, 1);

                word = (await db.GetAllWordsAsync())[0];
                // 核心公理断言：改判评分仅更新 FSRS 内部稳定性/难度，但 State 恒为 Mastered 且 NextReviewDate 恒为 null
                Assert.Equal((int)WordLearningState.Mastered, word.State);
                Assert.Null(word.NextReviewDate);

                // 主动撤销打卡
                await db.RevertTodayReviewAsync(todayLogs[0].LogId);
                word = (await db.GetAllWordsAsync())[0];
                Assert.Equal((int)WordLearningState.Mastered, word.State);
                Assert.Null(word.NextReviewDate);
            }
            finally
            {
                if (System.IO.File.Exists(testDb))
                {
                    try { System.IO.File.Delete(testDb); } catch { }
                }
            }
        }
    }
}
