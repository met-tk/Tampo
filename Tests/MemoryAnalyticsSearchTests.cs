using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class MemoryAnalyticsSearchTests
    {
        [Fact]
        public async Task SearchWordsAsync_ReturnsAllMatchingWordsAcrossTheHundredWordBoundary()
        {
            string testDb = Path.Combine(Path.GetTempPath(), $"tampo_analytics_{Guid.NewGuid():N}.db");
            try
            {
                var db = new DatabaseService(testDb);
                await db.InitializeAsync();
                await db.AddWordsAsync(
                    Enumerable.Range(0, 120).Select(i => $"词条_{i:D3}")
                        .Concat(Enumerable.Range(0, 10).Select(i => $"其他_{i:D3}")));

                var words = await db.GetAllWordsAsync();
                var list = await db.CreateWordListAsync("分析测试词单");
                await db.MoveWordsToListAsync(
                    words.Where(w => w.Text.StartsWith("词条_", StringComparison.Ordinal))
                        .Take(105).Select(w => w.Id), list.Id);

                var mastered = await db.GetWordByIdAsync(words.First(w => w.Text == "词条_000").Id)
                    ?? throw new InvalidOperationException("Test word was not saved.");
                mastered.State = (int)WordLearningState.Mastered;
                await db.UpdateWordAsync(mastered);

                var all = await db.SearchWordsAsync("");
                Assert.Equal(130, all.Count);
                Assert.Contains(all, w => w.Id == mastered.Id);
                Assert.Equal(120, (await db.SearchWordsAsync("词条_")).Count);

                var listWords = await db.SearchWordsAsync("", list.Id);
                Assert.Equal(105, listWords.Count);
                Assert.All(listWords, w => Assert.Equal(list.Id, w.WordListId));
                Assert.Equal(105, (await db.SearchWordsAsync("词条_", list.Id)).Count);
            }
            finally
            {
                foreach (var path in new[] { testDb, testDb + "-wal", testDb + "-shm" })
                {
                    try { File.Delete(path); } catch (IOException) { }
                }
            }
        }
    }
}
