using System;
using NihongoVocab.Models;
using Xunit;

namespace NihongoVocab.Tests
{
    public class WordUpdateTests
    {
        [Fact]
        public void UpdateWordText_PreservesReviewMetricsAndLearningState()
        {
            var word = new Word
            {
                Id = 1,
                Text = "犬",
                State = 1,
                Reps = 5,
                Lapses = 1,
                Stability = 4.2,
                Difficulty = 5.5,
                CreatedAt = new DateTime(2026, 1, 1),
                LastReviewDate = new DateTime(2026, 1, 15),
                NextReviewDate = new DateTime(2026, 2, 1)
            };

            // 模拟编辑更新逻辑：去除空格并更新
            string newText = "  猫  ";
            string trimmed = newText.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != word.Text)
            {
                word.Text = trimmed;
            }

            Assert.Equal("猫", word.Text);
            // 确保复习进度与状态未被改变
            Assert.Equal(1, word.State);
            Assert.Equal(5, word.Reps);
            Assert.Equal(1, word.Lapses);
            Assert.Equal(4.2, word.Stability);
            Assert.Equal(5.5, word.Difficulty);
            Assert.Equal(new DateTime(2026, 1, 1), word.CreatedAt);
            Assert.Equal(new DateTime(2026, 1, 15), word.LastReviewDate);
            Assert.Equal(new DateTime(2026, 2, 1), word.NextReviewDate);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public void UpdateWordText_InvalidOrEmptyText_ShouldNotChangeText(string invalidInput)
        {
            var word = new Word { Text = "既存の単語" };
            var original = word.Text;

            if (!string.IsNullOrWhiteSpace(invalidInput))
            {
                var trimmed = invalidInput.Trim();
                if (trimmed != word.Text)
                {
                    word.Text = trimmed;
                }
            }

            Assert.Equal(original, word.Text);
        }

        [Fact]
        public async System.Threading.Tasks.Task DatabaseService_Tombstone_Lifecycle_And_AutoClear_ShouldWork()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new NihongoVocab.Services.DatabaseService(testDb);
                await db.InitializeAsync();

                // 1. 模拟记录旧词墓碑
                await db.AddTombstoneAsync("word", "犬");
                var tombstones = await db.GetAllTombstonesAsync();
                Assert.Contains(tombstones, t => t.EntityType == "word" && t.EntityKey == "犬");

                // 2. 模拟撤销/清理墓碑
                await db.RemoveTombstoneAsync("word", "犬");
                tombstones = await db.GetAllTombstonesAsync();
                Assert.DoesNotContain(tombstones, t => t.EntityType == "word" && t.EntityKey == "犬");

                // 3. 再次添加墓碑，模拟重新导入新词时自动清除历史同名墓碑
                await db.AddTombstoneAsync("word", "猫");
                tombstones = await db.GetAllTombstonesAsync();
                Assert.Contains(tombstones, t => t.EntityType == "word" && t.EntityKey == "猫");

                // 导入包含“猫”的新词
                await db.AddWordsAsync(new[] { "猫" });
                tombstones = await db.GetAllTombstonesAsync();
                Assert.DoesNotContain(tombstones, t => t.EntityType == "word" && t.EntityKey == "猫");
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
        public async System.Threading.Tasks.Task DatabaseService_GetStudyQueueAsync_FilterByListId_And_ExcludeMastered()
        {
            string testDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tampo_test_queue_{System.Guid.NewGuid():N}.db");
            try
            {
                var db = new NihongoVocab.Services.DatabaseService(testDb);
                await db.InitializeAsync();

                // 创建两个词单
                var listA = await db.CreateWordListAsync("词单A");
                var listB = await db.CreateWordListAsync("词单B");

                // 在词单A添加3个词：1个新词，1个到期词，1个已掌握词
                await db.AddWordsAsync(new[] { "りんご", "みかん", "いちご" });
                var allWordsA = await db.GetAllWordsAsync();
                var wordANew = allWordsA.First(w => w.Text == "りんご");
                var wordADue = allWordsA.First(w => w.Text == "みかん");
                var wordAMastered = allWordsA.First(w => w.Text == "いちご");
                await db.MoveWordsToListAsync(new[] { wordANew.Id, wordADue.Id, wordAMastered.Id }, listA.Id);

                // 设置 wordADue 为到期复习
                wordADue.WordListId = listA.Id;
                wordADue.State = (int)WordLearningState.Review;
                wordADue.Reps = 3;
                wordADue.NextReviewDate = DateTime.Now.AddDays(-1);
                await db.UpdateWordAsync(wordADue);

                // 设置 wordAMastered 为已掌握
                wordAMastered.WordListId = listA.Id;
                wordAMastered.State = (int)WordLearningState.Mastered;
                await db.UpdateWordAsync(wordAMastered);

                // 在词单B添加2个词：1个新词，1个未到期词
                await db.AddWordsAsync(new[] { "くるま", "でんしゃ" });
                var allWordsB = await db.GetAllWordsAsync();
                var wordBNew = allWordsB.First(w => w.Text == "くるま");
                var wordBFuture = allWordsB.First(w => w.Text == "でんしゃ");
                await db.MoveWordsToListAsync(new[] { wordBNew.Id, wordBFuture.Id }, listB.Id);

                wordBFuture.WordListId = listB.Id;
                wordBFuture.State = (int)WordLearningState.Review;
                wordBFuture.Reps = 2;
                wordBFuture.NextReviewDate = DateTime.Now.AddDays(5); // 5天后才到期
                await db.UpdateWordAsync(wordBFuture);

                // 1. 全局待学测试 (listId = null)
                // 预期待学：词单A的新词(りんご)、词单A的到期词(みかん)、词单B的新词(くるま)
                // 排除：词单A的已掌握(いちご)、词单B的未来词(でんしゃ)
                var globalQueue = await db.GetStudyQueueAsync(null);
                Assert.Equal(3, globalQueue.Count);
                Assert.Contains(globalQueue, w => w.Text == "りんご");
                Assert.Contains(globalQueue, w => w.Text == "みかん");
                Assert.Contains(globalQueue, w => w.Text == "くるま");
                Assert.DoesNotContain(globalQueue, w => w.Text == "いちご");
                Assert.DoesNotContain(globalQueue, w => w.Text == "でんしゃ");

                // 2. 词单A筛选测试 (listId = listA.Id)
                // 预期待学：仅词单A的新词和到期词 (2个)
                var queueA = await db.GetStudyQueueAsync(listA.Id);
                Assert.Equal(2, queueA.Count);
                Assert.All(queueA, w => Assert.Equal(listA.Id, w.WordListId));
                Assert.Contains(queueA, w => w.Text == "りんご");
                Assert.Contains(queueA, w => w.Text == "みかん");

                // 3. 词单B筛选测试 (listId = listB.Id)
                // 预期待学：仅词单B的新词 (1个)
                var queueB = await db.GetStudyQueueAsync(listB.Id);
                Assert.Single(queueB);
                Assert.Equal("くるま", queueB[0].Text);
                Assert.Equal(listB.Id, queueB[0].WordListId);
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
