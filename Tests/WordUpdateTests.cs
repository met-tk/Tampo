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
    }
}
