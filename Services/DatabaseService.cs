using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public class DashboardStats
    {
        public int TotalWords { get; set; }
        public int TotalImportDays { get; set; }
        public int TotalStudyDays { get; set; }
        public int NewCount { get; set; }
        public int LearningCount { get; set; }
        public int ReviewCount { get; set; }
        public int MasteredCount { get; set; }
        public double AverageRetention { get; set; }

        // FSRS 核心算法解耦与 4 态指标（严格排除独立掌握归档池）
        public int FsrsActiveCount { get; set; }
        public int FsrsNewCount { get; set; }
        public int FsrsLearningCount { get; set; }
        public int FsrsReviewCount { get; set; }
        public int FsrsRelearningCount { get; set; }
        public double AverageStability { get; set; }
        public double AverageDifficulty { get; set; }
    }

    [Table("UserPreferences")]
    public class UserPreference
    {
        [PrimaryKey]
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class DayActivityDetail
    {
        public DateTime Date { get; set; }
        public int ImportedCount { get; set; }
        public List<string> SampleImportedWords { get; set; } = new();
        public List<Word> ImportedWords { get; set; } = new();
        public int ReviewCount { get; set; }
        public int RememberCount { get; set; }
        public int ForgetCount { get; set; }
        public List<TodayReviewItem> ReviewedItems { get; set; } = new();
    }

    public class ImportResult
    {
        public int TotalInput { get; set; }
        public int InsertedCount { get; set; }
        public int DuplicatedCount { get; set; }
        public List<string> DuplicatedSamples { get; set; } = new();
    }

    public class TodayReviewItem
    {
        public int LogId { get; set; }
        public int WordId { get; set; }
        public string WordText { get; set; } = string.Empty;
        public int Rating { get; set; }
        public DateTime ReviewDate { get; set; }
        public string RatingText => Rating == 3 ? "记得" : "遗忘";
        public string ReviewTimeText => ReviewDate.ToString("HH:mm:ss");
    }

    public class DatabaseService
    {
        public static event Action? DataChanged;
        public static void NotifyDataChanged() => DataChanged?.Invoke();

        private SQLiteAsyncConnection? _database;
        private readonly string _dbPath;
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        private class TableColumnInfo
        {
            public string name { get; set; } = string.Empty;
        }

        public DatabaseService() : this(null)
        {
        }

        public DatabaseService(string? customDbPath)
        {
            SQLitePCL.Batteries_V2.Init();

            if (!string.IsNullOrEmpty(customDbPath))
            {
                _dbPath = customDbPath;
                string? dir = Path.GetDirectoryName(customDbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            else
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NihongoVocab");

                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                _dbPath = Path.Combine(appDataDir, "vocab.db");
            }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await _dbLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                _database = new SQLiteAsyncConnection(_dbPath);

                await _database.CreateTableAsync<Word>();
                await _database.CreateTableAsync<WordList>();
                await _database.CreateTableAsync<ReviewLog>();
                await _database.CreateTableAsync<UserPreference>();
                await _database.CreateTableAsync<SyncTombstone>();

                // 字段平滑回填迁移保障 (StateUpdatedAt, MetaUpdatedAt, UpdatedAt)
                try
                {
                    var wordCols = (await _database.QueryAsync<TableColumnInfo>("PRAGMA table_info(Words)"))
                        .Select(c => c.name.ToLowerInvariant()).ToHashSet();
                    if (!wordCols.Contains("stateupdatedat"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE Words ADD COLUMN StateUpdatedAt TEXT");
                        await _database.ExecuteAsync("UPDATE Words SET StateUpdatedAt = coalesce(LastReviewDate, CreatedAt, datetime('now', 'localtime')) WHERE StateUpdatedAt IS NULL");
                    }
                    if (!wordCols.Contains("metaupdatedat"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE Words ADD COLUMN MetaUpdatedAt TEXT");
                        await _database.ExecuteAsync("UPDATE Words SET MetaUpdatedAt = coalesce(CreatedAt, datetime('now', 'localtime')) WHERE MetaUpdatedAt IS NULL");
                    }

                    var listCols = (await _database.QueryAsync<TableColumnInfo>("PRAGMA table_info(WordLists)"))
                        .Select(c => c.name.ToLowerInvariant()).ToHashSet();
                    if (!listCols.Contains("updatedat"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE WordLists ADD COLUMN UpdatedAt TEXT");
                        await _database.ExecuteAsync("UPDATE WordLists SET UpdatedAt = coalesce(CreatedAt, datetime('now', 'localtime')) WHERE UpdatedAt IS NULL");
                    }

                    var logCols = (await _database.QueryAsync<TableColumnInfo>("PRAGMA table_info(ReviewLogs)"))
                        .Select(c => c.name.ToLowerInvariant()).ToHashSet();
                    if (!logCols.Contains("state"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE ReviewLogs ADD COLUMN State INTEGER NOT NULL DEFAULT 0");
                    }
                    if (!logCols.Contains("elapseddays"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE ReviewLogs ADD COLUMN ElapsedDays INTEGER NOT NULL DEFAULT 0");
                    }
                    if (!logCols.Contains("scheduleddays"))
                    {
                        await _database.ExecuteAsync("ALTER TABLE ReviewLogs ADD COLUMN ScheduledDays INTEGER NOT NULL DEFAULT 0");
                    }

                    DateTime expireCutoff = DateTime.Now.AddDays(-90);
                    await _database.Table<SyncTombstone>().DeleteAsync(t => t.DeletedAt < expireCutoff);
                }
                catch (Exception migEx)
                {
                    CrashLogger.LogException(migEx, "DatabaseService.InitializeAsync.Migration");
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.InitializeAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        #region 单词管理与事务写入

        public async Task<ImportResult> AddWordsWithResultAsync(IEnumerable<string> words)
        {
            await EnsureInitializedAsync();
            var result = new ImportResult();
            DateTime now = DateTime.Now;

            await _dbLock.WaitAsync();
            try
            {
                var existingWords = new HashSet<string>(
                    (await _database!.Table<Word>().ToListAsync()).Select(w => w.Text),
                    StringComparer.OrdinalIgnoreCase);

                var toInsert = new List<Word>();
                foreach (var w in words)
                {
                    string trimmed = w.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    result.TotalInput++;

                    if (existingWords.Contains(trimmed))
                    {
                        result.DuplicatedCount++;
                        if (result.DuplicatedSamples.Count < 10)
                        {
                            result.DuplicatedSamples.Add(trimmed);
                        }
                    }
                    else
                    {
                        existingWords.Add(trimmed);
                        toInsert.Add(new Word
                        {
                            Text = trimmed,
                            CreatedAt = now,
                            IsInList = false,
                            WordListId = null,
                            State = (int)WordLearningState.New,
                            Stability = 0.0,
                            Difficulty = 0.0,
                            Reps = 0,
                            Lapses = 0
                        });
                    }
                }

                if (toInsert.Count > 0)
                {
                    // 采用事务批量插入，保证高并发及写入稳定性
                    await _database!.RunInTransactionAsync(conn =>
                    {
                        foreach (var item in toInsert)
                        {
                            conn.Insert(item);
                            string cleanKey = item.Text.Trim().ToLowerInvariant();
                            conn.Table<SyncTombstone>().Delete(t => t.EntityType == "word" && t.EntityKey == cleanKey);
                        }
                    });
                    result.InsertedCount = toInsert.Count;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.AddWordsWithResultAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            if (result.InsertedCount > 0)
            {
                DataChanged?.Invoke();
            }

            return result;
        }

        public async Task<int> AddWordsAsync(IEnumerable<string> words)
        {
            var res = await AddWordsWithResultAsync(words);
            return res.InsertedCount;
        }

        public async Task<List<Word>> GetAllWordsAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var words = await _database!.Table<Word>().OrderByDescending(w => w.CreatedAt).ToListAsync();
                var lists = await _database!.Table<WordList>().ToListAsync();
                var dict = lists.ToDictionary(l => l.Id, l => l.Name);

                foreach (var w in words)
                {
                    if (w.WordListId.HasValue && dict.TryGetValue(w.WordListId.Value, out var listName))
                    {
                        w.WordListName = listName;
                    }
                }

                return words;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetAllWordsAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> GetUnlistedWordsAsync(DateTime? startDate, DateTime? endDate)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var query = _database!.Table<Word>().Where(w => !w.IsInList);

                if (startDate.HasValue)
                {
                    DateTime start = startDate.Value.Date;
                    query = query.Where(w => w.CreatedAt >= start);
                }

                if (endDate.HasValue)
                {
                    DateTime end = endDate.Value.Date.AddDays(1).AddTicks(-1);
                    query = query.Where(w => w.CreatedAt <= end);
                }

                return await query.OrderByDescending(w => w.CreatedAt).ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetUnlistedWordsAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> GetWordsByListIdAsync(int listId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<Word>()
                    .Where(w => w.WordListId == listId)
                    .OrderBy(w => w.Id)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetWordsByListIdAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task RemoveWordFromListAsync(int wordId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var word = await _database!.FindAsync<Word>(wordId);
                if (word != null)
                {
                    word.IsInList = false;
                    word.WordListId = null;
                    word.WordListName = null;
                    word.MetaUpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(word);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.RemoveWordFromListAsync");
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task DeleteWordListAsync(int listId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var targetList = await _database!.FindAsync<WordList>(listId);
                if (targetList != null)
                {
                    // 记录词单墓碑，防止对端重新同步时词单无限复活
                    await AddTombstoneInternalAsync("word_list", targetList.Name.Trim().ToLowerInvariant());
                }

                var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                foreach (var w in words)
                {
                    w.IsInList = false;
                    w.WordListId = null;
                    w.WordListName = null;
                    w.MetaUpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(w);
                }
                await _database!.DeleteAsync<WordList>(listId);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.DeleteWordListAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task DeleteWordListCompletelyAsync(int listId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var targetList = await _database!.FindAsync<WordList>(listId);
                if (targetList != null)
                {
                    await AddTombstoneInternalAsync("word_list", targetList.Name.Trim().ToLowerInvariant());
                }

                var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                foreach (var w in words)
                {
                    await _database.Table<ReviewLog>().Where(r => r.WordId == w.Id).DeleteAsync();
                    await _database.DeleteAsync(w);

                    // 彻底删除单词时同步写入单词墓碑
                    await AddTombstoneInternalAsync("word", w.Text.Trim().ToLowerInvariant());
                }
                await _database!.DeleteAsync<WordList>(listId);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.DeleteWordListCompletelyAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task RenameWordListAsync(int listId, string newName)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var list = await _database!.FindAsync<WordList>(listId);
                if (list != null)
                {
                    string oldName = list.Name.Trim();
                    string cleanNew = newName.Trim();
                    if (!oldName.Equals(cleanNew, StringComparison.OrdinalIgnoreCase))
                    {
                        await AddTombstoneInternalAsync("word_list", oldName.ToLowerInvariant());
                        await RemoveTombstoneInternalAsync("word_list", cleanNew.ToLowerInvariant());
                    }

                    list.Name = cleanNew;
                    list.UpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(list);

                    var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                    foreach (var w in words)
                    {
                        w.WordListName = cleanNew;
                        w.MetaUpdatedAt = DateTime.Now;
                        await _database!.UpdateAsync(w);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.RenameWordListAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task MergeWordListsAsync(int sourceListId, int targetListId)
        {
            if (sourceListId == targetListId) return;
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var targetList = await _database!.FindAsync<WordList>(targetListId);
                if (targetList == null) return;

                var sourceList = await _database!.FindAsync<WordList>(sourceListId);
                if (sourceList != null)
                {
                    await AddTombstoneInternalAsync("word_list", sourceList.Name.Trim().ToLowerInvariant());
                }

                targetList.UpdatedAt = DateTime.Now;
                await _database!.UpdateAsync(targetList);

                var sourceWords = await _database!.Table<Word>().Where(w => w.WordListId == sourceListId).ToListAsync();
                foreach (var w in sourceWords)
                {
                    w.WordListId = targetListId;
                    w.WordListName = targetList.Name;
                    w.MetaUpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(w);
                }

                await _database!.DeleteAsync<WordList>(sourceListId);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.MergeWordListsAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task MoveWordsToListAsync(IEnumerable<int> wordIds, int? targetListId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                string? listName = null;
                if (targetListId.HasValue)
                {
                    var targetList = await _database!.FindAsync<WordList>(targetListId.Value);
                    listName = targetList?.Name;
                }

                var idSet = new HashSet<int>(wordIds);
                var words = await _database!.Table<Word>().Where(w => idSet.Contains(w.Id)).ToListAsync();
                foreach (var w in words)
                {
                    w.WordListId = targetListId;
                    w.IsInList = targetListId.HasValue;
                    w.WordListName = listName;
                    w.MetaUpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(w);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.MoveWordsToListAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task UpdateWordsStateAsync(IEnumerable<int> wordIds, WordLearningState newState)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var idSet = new HashSet<int>(wordIds);
                var words = (await _database!.Table<Word>().ToListAsync()).Where(w => idSet.Contains(w.Id)).ToList();
                DateTime now = DateTime.Now;

                foreach (var w in words)
                {
                    // 软件支持的手动标记单词状态严格仅限于两种：【未学习 (New)】和【已掌握 (Mastered)】
                    if (newState == WordLearningState.Mastered)
                    {
                        // 【已掌握】独立于 FSRS 算法之外：仅设置状态与免复习，不伪造或覆盖 Stability / LastReviewDate
                        w.State = (int)WordLearningState.Mastered;
                        w.NextReviewDate = null;
                        w.StateUpdatedAt = now;
                    }
                    else
                    {
                        // 手动重置为【未学习 (New)】：清空复习历史并写入日志墓碑，重置全部 FSRS 状态参数
                        var oldLogs = await _database.Table<ReviewLog>().Where(l => l.WordId == w.Id).ToListAsync();
                        foreach (var oldLog in oldLogs)
                        {
                            DateTime localRev = oldLog.ReviewDate.Kind == DateTimeKind.Utc ? oldLog.ReviewDate.ToLocalTime() : oldLog.ReviewDate;
                            string tombKey = $"{w.Text.Trim().ToLowerInvariant()}_{localRev:yyyyMMddHHmmss}";
                            await AddTombstoneInternalAsync("review_log", tombKey, now);
                            await _database.DeleteAsync(oldLog);
                        }
                        w.State = (int)WordLearningState.New;
                        w.Stability = 0;
                        w.Difficulty = 0;
                        w.Reps = 0;
                        w.Lapses = 0;
                        w.LastReviewDate = null;
                        w.NextReviewDate = null;
                        w.StateUpdatedAt = now;
                    }
                    await _database!.UpdateAsync(w);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.UpdateWordsStateAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task<int> AddWordAsync(Word word)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                if (word.StateUpdatedAt == default) word.StateUpdatedAt = DateTime.Now;
                if (word.MetaUpdatedAt == default) word.MetaUpdatedAt = DateTime.Now;
                int res = await _database!.InsertAsync(word);
                return res;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.AddWordAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<int> UpdateWordAsync(Word word)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                if (word.StateUpdatedAt == default) word.StateUpdatedAt = DateTime.Now;
                if (word.MetaUpdatedAt == default) word.MetaUpdatedAt = DateTime.Now;
                int res = await _database!.UpdateAsync(word);
                return res;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.UpdateWordAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<int> UpdateWordListAsync(WordList list)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                if (list.UpdatedAt == default) list.UpdatedAt = DateTime.Now;
                int res = await _database!.UpdateAsync(list);
                return res;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.UpdateWordListAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> GetWordsByIdsAsync(IEnumerable<int> ids)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var idSet = new HashSet<int>(ids);
                return (await _database!.Table<Word>().ToListAsync())
                    .Where(w => idSet.Contains(w.Id))
                    .OrderBy(w => w.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetWordsByIdsAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> GetStudyQueueAsync(int? listId = null)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var query = _database!.Table<Word>();
                if (listId.HasValue)
                {
                    query = query.Where(w => w.WordListId == listId.Value);
                }

                // 严格排除已掌握（Mastered）单词：已掌握词彻底终结复习周期，不参与任何日常复习
                query = query.Where(w => w.State != (int)WordLearningState.Mastered);

                var allCandidates = await query.ToListAsync();
                DateTime today = DateTime.Today;
                DateTime endOfToday = today.AddDays(1).AddTicks(-1);

                // 待学习/复习队列规则：
                // 1. 从未学习过的新词（Reps == 0）且今日未复习
                // 2. 到期需要复习的词（NextReviewDate 为空或 <= endOfToday），并且严格排除今日已经复习过的词（LastReviewDate?.Date == today），彻底杜绝一天之内重复学习！
                var queue = allCandidates
                    .Where(w => (!w.LastReviewDate.HasValue || w.LastReviewDate.Value.Date < today) &&
                                (w.Reps == 0 || !w.NextReviewDate.HasValue || w.NextReviewDate.Value <= endOfToday))
                    .OrderBy(w => w.Reps == 0 ? 0 : 1)
                    .ThenBy(w => w.NextReviewDate ?? DateTime.MinValue)
                    .ThenBy(w => w.CreatedAt)
                    .ToList();

                // 决不回退取全部词汇：学完就是学完，返回空集合以展示今日复习已全部完成
                return queue;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetStudyQueueAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task UpdateWordFSRSAsync(Word word, int rating, DateTime now)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                DateTime localNow = now.Kind == DateTimeKind.Utc ? now.ToLocalTime() : now;
                DateTime dayStart = localNow.Date;
                DateTime dayEnd = dayStart.AddDays(1).AddTicks(-1);

                var wordLogs = (await _database!.Table<ReviewLog>()
                    .Where(l => l.WordId == word.Id)
                    .ToListAsync())
                    .OrderBy(l => l.ReviewDate)
                    .ThenBy(l => l.Id)
                    .ToList();

                var sameDayLog = wordLogs.LastOrDefault(l =>
                {
                    DateTime d = l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate;
                    return d >= dayStart && d <= dayEnd;
                });

                if (sameDayLog != null)
                {
                    // 同日幂等防重：同一自然日内对同一单词重复提交复习时，基于当日首次打卡前的状态原地重算并更新该日志，绝不重复追加日志或虚增 Reps
                    var priorLogs = wordLogs.Where(l =>
                    {
                        DateTime d = l.ReviewDate.Kind == DateTimeKind.Utc ? l.ReviewDate.ToLocalTime() : l.ReviewDate;
                        return d < dayStart && l.Id != sameDayLog.Id;
                    }).ToList();

                    int preReps = priorLogs.Count;
                    int preLapses = priorLogs.Count(l => l.Rating == 1);
                    var prevLog = priorLogs.LastOrDefault();

                    double preS = preReps > 0
                        ? (sameDayLog.StabilityBefore > 0 ? sameDayLog.StabilityBefore : (prevLog?.StabilityAfter ?? 0.0))
                        : 0.0;
                    double preD = preReps > 0
                        ? (sameDayLog.DifficultyBefore > 0 ? sameDayLog.DifficultyBefore : (prevLog?.DifficultyAfter ?? 0.0))
                        : 0.0;
                    DateTime? preLastRev = prevLog?.ReviewDate;
                    int preState = preReps == 0
                        ? (int)WordLearningState.New
                        : ((preReps < 2 || (prevLog != null && prevLog.Rating == 1))
                            ? (int)WordLearningState.Learning
                            : (int)WordLearningState.Review);

                    var preWord = new Word
                    {
                        Id = word.Id,
                        Text = word.Text,
                        CreatedAt = word.CreatedAt,
                        State = preState,
                        Stability = preS,
                        Difficulty = preD,
                        Reps = preReps,
                        Lapses = preLapses,
                        LastReviewDate = preLastRev
                    };

                    int elapsedDays = preReps == 0
                        ? 0
                        : (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(preLastRev ?? word.CreatedAt, localNow));
                    var (newS, newD, nextReview) = FsrsEngine.Review(preWord, rating, localNow);
                    int scheduledDays = Math.Max(1, (int)Math.Round((nextReview.Date - localNow.Date).TotalDays));

                    word.Stability = newS;
                    word.Difficulty = newD;
                    word.LastReviewDate = localNow;
                    word.Reps = preReps + 1;
                    word.Lapses = preLapses + (rating == 1 ? 1 : 0);

                    if (word.State == (int)WordLearningState.Mastered)
                    {
                        word.NextReviewDate = null;
                    }
                    else
                    {
                        word.NextReviewDate = nextReview;
                        word.State = (rating == 1 || word.Reps < 2)
                            ? (int)WordLearningState.Learning
                            : (int)WordLearningState.Review;
                    }

                    word.StateUpdatedAt = localNow;
                    await _database!.UpdateAsync(word);

                    sameDayLog.Rating = rating;
                    sameDayLog.StabilityBefore = preS;
                    sameDayLog.StabilityAfter = newS;
                    sameDayLog.DifficultyBefore = preD;
                    sameDayLog.DifficultyAfter = newD;
                    sameDayLog.State = preState;
                    sameDayLog.ElapsedDays = elapsedDays;
                    sameDayLog.ScheduledDays = scheduledDays;
                    await _database!.UpdateAsync(sameDayLog);
                }
                else
                {
                    int prevState = word.State;
                    double oldS = word.Stability;
                    double oldD = word.Difficulty;
                    int elapsedDays = word.Reps == 0
                        ? 0
                        : (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(word.LastReviewDate ?? word.CreatedAt, localNow));

                    var (newS, newD, nextReview) = FsrsEngine.Review(word, rating, localNow);
                    int scheduledDays = Math.Max(1, (int)Math.Round((nextReview.Date - localNow.Date).TotalDays));

                    word.Stability = newS;
                    word.Difficulty = newD;
                    word.LastReviewDate = localNow;
                    bool isFirstTime = word.Reps == 0;
                    word.Reps += 1;

                    if (rating == 1)
                    {
                        word.Lapses += 1;
                    }

                    if (word.State == (int)WordLearningState.Mastered)
                    {
                        // 用户已手动指定为【已掌握】，保持独立分类且免除下次复习排期
                        word.NextReviewDate = null;
                    }
                    else
                    {
                        word.NextReviewDate = nextReview;
                        if (rating == 1 || isFirstTime || word.Reps < 2)
                        {
                            word.State = (int)WordLearningState.Learning;
                        }
                        else
                        {
                            word.State = (int)WordLearningState.Review;
                        }
                    }

                    word.StateUpdatedAt = localNow;
                    await _database!.UpdateAsync(word);

                    var log = new ReviewLog
                    {
                        WordId = word.Id,
                        Rating = rating,
                        ReviewDate = localNow,
                        StabilityBefore = oldS,
                        StabilityAfter = newS,
                        DifficultyBefore = oldD,
                        DifficultyAfter = newD,
                        State = prevState,
                        ElapsedDays = elapsedDays,
                        ScheduledDays = scheduledDays
                    };
                    await _database!.InsertAsync(log);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.UpdateWordFSRSAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        #endregion

        #region 词单管理

        public Task<WordList> CreateWordListAsync(string name) => CreateWordListAsync(name, Enumerable.Empty<int>());

        public async Task<WordList> CreateWordListAsync(string name, IEnumerable<int> wordIds)
        {
            await EnsureInitializedAsync();
            var idList = wordIds.ToList();

            await _dbLock.WaitAsync();
            try
            {
                // 新建词单时撤销历史删除墓碑，防止被对端旧墓碑删除
                await RemoveTombstoneInternalAsync("word_list", name.Trim().ToLowerInvariant());

                // 防重兜底保险：如果5秒内已有同名新词单被创建，直接复用，防止任何重入并发导致生成多个词单
                var recentThreshold = DateTime.Now.AddSeconds(-5);
                var existing = await _database!.Table<WordList>()
                    .Where(l => l.Name == name && l.CreatedAt >= recentThreshold)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    CrashLogger.LogInfo($"CreateWordListAsync: duplicate creation prevented for '{name}' (ID={existing.Id})");
                    return existing;
                }

                var list = new WordList
                {
                    Name = name,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    WordCount = idList.Count
                };

                await _database!.InsertAsync(list);

                if (idList.Count > 0)
                {
                    await _database!.RunInTransactionAsync(conn =>
                    {
                        foreach (var id in idList)
                        {
                            var w = conn.Find<Word>(id);
                            if (w != null)
                            {
                                w.IsInList = true;
                                w.WordListId = list.Id;
                                w.WordListName = list.Name;
                                w.MetaUpdatedAt = DateTime.Now;
                                conn.Update(w);
                            }
                        }
                    });
                }

                CrashLogger.LogInfo($"CreateWordListAsync: created list '{name}' (ID={list.Id}) with {idList.Count} words");
                DataChanged?.Invoke();
                return list;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.CreateWordListAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<WordList>> GetAllWordListsAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var lists = await _database!.Table<WordList>().OrderByDescending(l => l.CreatedAt).ToListAsync();
                var allWords = await _database!.Table<Word>().ToListAsync();
                DateTime today = DateTime.Today;
                DateTime endOfToday = today.AddDays(1).AddTicks(-1);

                foreach (var l in lists)
                {
                    var wordsInThis = allWords.Where(w => w.WordListId == l.Id).ToList();
                    l.WordCount = wordsInThis.Count;
                    l.UnreviewedCount = wordsInThis.Count(w =>
                        w.State != (int)WordLearningState.Mastered &&
                        (!w.LastReviewDate.HasValue || w.LastReviewDate.Value.Date < today) &&
                        (w.Reps == 0 || !w.NextReviewDate.HasValue || w.NextReviewDate.Value <= endOfToday));
                }

                return lists;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetAllWordListsAsync");
                return new List<WordList>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        #region 双日历热力图与统计分析

        public async Task<Dictionary<DateTime, int>> GetImportHeatmapDataAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var words = await _database!.Table<Word>().ToListAsync();
                var result = new Dictionary<DateTime, int>();

                foreach (var w in words)
                {
                    DateTime day = w.CreatedAt.Date;
                    if (result.ContainsKey(day))
                    {
                        result[day]++;
                    }
                    else
                    {
                        result[day] = 1;
                    }
                }

                return result;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<Dictionary<DateTime, int>> GetStudyHeatmapDataAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var logs = await _database!.Table<ReviewLog>().ToListAsync();
                var result = new Dictionary<DateTime, int>();

                foreach (var l in logs)
                {
                    DateTime day = l.ReviewDate.Date;
                    if (result.ContainsKey(day))
                    {
                        result[day]++;
                    }
                    else
                    {
                        result[day] = 1;
                    }
                }

                return result;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<(Dictionary<DateTime, List<string>> Imported, Dictionary<DateTime, List<string>> Studied)> GetCalendarActivityDetailsDictAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var words = await _database!.Table<Word>().ToListAsync();
                var logs = await _database!.Table<ReviewLog>().ToListAsync();

                var wordDict = words.ToDictionary(w => w.Id, w => w.Text);

                var imported = words.GroupBy(w => w.CreatedAt.Date)
                                    .ToDictionary(g => g.Key, g => g.Select(w => w.Text).ToList());

                var studied = logs.GroupBy(l => l.ReviewDate.Date)
                                  .ToDictionary(g => g.Key, g => g.Where(l => wordDict.ContainsKey(l.WordId))
                                                                 .Select(l => wordDict[l.WordId])
                                                                 .Distinct()
                                                                 .ToList());

                return (imported, studied);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetCalendarActivityDetailsDictAsync");
                return (new(), new());
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<DayActivityDetail> GetDayDetailAsync(DateTime date)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                DateTime dayStart = date.Date;
                DateTime dayEnd = dayStart.AddDays(1).AddTicks(-1);

                var words = await _database!.Table<Word>()
                    .Where(w => w.CreatedAt >= dayStart && w.CreatedAt <= dayEnd)
                    .OrderBy(w => w.Id)
                    .ToListAsync();

                var logs = await _database!.Table<ReviewLog>()
                    .Where(l => l.ReviewDate >= dayStart && l.ReviewDate <= dayEnd)
                    .OrderByDescending(l => l.ReviewDate)
                    .ToListAsync();

                var reviewedWordIds = logs.Select(l => l.WordId).Distinct().ToList();
                var reviewedWordsMap = new Dictionary<int, Word>();
                if (reviewedWordIds.Count > 0)
                {
                    var reviewedWords = await _database!.Table<Word>()
                        .Where(w => reviewedWordIds.Contains(w.Id))
                        .ToListAsync();
                    reviewedWordsMap = reviewedWords.ToDictionary(w => w.Id);
                }

                var reviewedItems = logs.Select(l => new TodayReviewItem
                {
                    LogId = l.Id,
                    WordId = l.WordId,
                    WordText = reviewedWordsMap.TryGetValue(l.WordId, out var rw) ? rw.Text : $"[词汇#{l.WordId}]",
                    Rating = l.Rating,
                    ReviewDate = l.ReviewDate
                }).ToList();

                return new DayActivityDetail
                {
                    Date = dayStart,
                    ImportedCount = words.Count,
                    SampleImportedWords = words.Select(w => w.Text).ToList(),
                    ImportedWords = words,
                    ReviewCount = logs.Count,
                    RememberCount = logs.Count(l => l.Rating == 3),
                    ForgetCount = logs.Count(l => l.Rating == 1),
                    ReviewedItems = reviewedItems
                };
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetDayDetailAsync");
                return new DayActivityDetail { Date = date };
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<Word?> GetWordByIdAsync(int id)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<Word>().FirstOrDefaultAsync(w => w.Id == id);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetWordByIdAsync");
                return null;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public Task<List<ReviewLog>> GetReviewLogsByWordIdAsync(int wordId) => GetWordReviewLogsAsync(wordId);

        public async Task<List<ReviewLog>> GetWordReviewLogsAsync(int wordId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<ReviewLog>()
                    .Where(l => l.WordId == wordId)
                    .OrderByDescending(l => l.ReviewDate)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetWordReviewLogsAsync");
                return new List<ReviewLog>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<ReviewLog>> GetAllReviewLogsAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<ReviewLog>().OrderBy(r => r.ReviewDate).ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetAllReviewLogsAsync");
                return new List<ReviewLog>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<int> AddReviewLogAsync(ReviewLog log)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.InsertAsync(log);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.AddReviewLogAsync");
                return 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<int> UpdateReviewLogAsync(ReviewLog log)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.UpdateAsync(log);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.UpdateReviewLogAsync");
                return 0;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> GetWordsByStateAsync(int state, int? wordListId = null)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var query = _database!.Table<Word>();
                if (wordListId.HasValue)
                {
                    query = query.Where(w => w.WordListId == wordListId.Value);
                }
                var all = await query.ToListAsync();

                // 排除已掌握独立分类，依据 FSRS 4 态统一调度算法严格过滤
                var targetFsrs = (FsrsScheduleState)state;
                return all.Where(w => w.State != (int)WordLearningState.Mastered && w.FsrsState == targetFsrs)
                          .OrderBy(w => w.Text)
                          .ToList();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetWordsByStateAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }


        public async Task<DashboardStats> GetDashboardStatsAsync(int? wordListId = null)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var query = _database!.Table<Word>();
                if (wordListId.HasValue)
                {
                    query = query.Where(w => w.WordListId == wordListId.Value);
                }

                var words = await query.ToListAsync();
                var wordIds = new HashSet<int>(words.Select(w => w.Id));
                var allLogs = await _database!.Table<ReviewLog>().ToListAsync();
                var logs = wordListId.HasValue ? allLogs.Where(l => wordIds.Contains(l.WordId)).ToList() : allLogs;

                var masteredWords = words.Where(w => w.State == (int)WordLearningState.Mastered).ToList();
                var fsrsWords = words.Where(w => w.State != (int)WordLearningState.Mastered).ToList();

                // FSRS 4 态科学调度细分统计（已掌握分类独立，绝不计入 FSRS 调度池）：
                // 1. 未学习 (New): w.FsrsState == FsrsScheduleState.New
                // 2. 初学中 (Learning): w.FsrsState == FsrsScheduleState.Learning
                // 3. 复习中 (Review): w.FsrsState == FsrsScheduleState.Review
                // 4. 重新学习 (Relearning): w.FsrsState == FsrsScheduleState.Relearning
                int fsrsNew = fsrsWords.Count(w => w.FsrsState == FsrsScheduleState.New);
                int fsrsLearning = fsrsWords.Count(w => w.FsrsState == FsrsScheduleState.Learning);
                int fsrsReview = fsrsWords.Count(w => w.FsrsState == FsrsScheduleState.Review);
                int fsrsRelearning = fsrsWords.Count(w => w.FsrsState == FsrsScheduleState.Relearning);

                var stats = new DashboardStats
                {
                    TotalWords = words.Count,
                    TotalImportDays = words.Select(w => w.CreatedAt.Date).Distinct().Count(),
                    TotalStudyDays = logs.Select(l => l.ReviewDate.Date).Distinct().Count(),
                    NewCount = words.Count(w => w.State == (int)WordLearningState.New),
                    LearningCount = words.Count(w => w.State == (int)WordLearningState.Learning),
                    ReviewCount = words.Count(w => w.State == (int)WordLearningState.Review),
                    MasteredCount = masteredWords.Count,

                    FsrsActiveCount = fsrsWords.Count,
                    FsrsNewCount = fsrsNew,
                    FsrsLearningCount = fsrsLearning,
                    FsrsReviewCount = fsrsReview,
                    FsrsRelearningCount = fsrsRelearning
                };

                // 核心 FSRS 算法指标：严格仅基于 FSRS 活跃复习词汇计算，已掌握词汇绝不混入
                var activeReviewedWords = fsrsWords.Where(w => w.Stability > 0).ToList();
                if (activeReviewedWords.Count > 0)
                {
                    DateTime now = DateTime.Now;
                    stats.AverageRetention = activeReviewedWords.Average(w => FsrsEngine.CalculateRetrievability(w.Stability, w.LastReviewDate ?? w.CreatedAt, now));
                    stats.AverageStability = activeReviewedWords.Average(w => w.Stability);
                    stats.AverageDifficulty = activeReviewedWords.Average(w => w.Difficulty);
                }
                else
                {
                    stats.AverageRetention = 0.0;
                    stats.AverageStability = 0.0;
                    stats.AverageDifficulty = 0.0;
                }

                return stats;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<List<Word>> SearchWordsAsync(string query, int? wordListId = null)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var q = _database!.Table<Word>();
                if (wordListId.HasValue)
                {
                    q = q.Where(w => w.WordListId == wordListId.Value);
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return await q.OrderBy(w => w.Id).Take(100).ToListAsync();
                }

                return await q
                    .Where(w => w.Text.Contains(query))
                    .OrderBy(w => w.Id)
                    .Take(100)
                    .ToListAsync();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        #region 当日复习记录与反悔更正

        public async Task<List<TodayReviewItem>> GetTodayReviewItemsAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                DateTime todayStart = DateTime.Today;
                DateTime todayEnd = todayStart.AddDays(1).AddTicks(-1);

                var logs = await _database!.Table<ReviewLog>()
                    .Where(l => l.ReviewDate >= todayStart && l.ReviewDate <= todayEnd)
                    .OrderByDescending(l => l.ReviewDate)
                    .ToListAsync();

                if (logs.Count == 0) return new List<TodayReviewItem>();

                var wordIds = logs.Select(l => l.WordId).Distinct().ToList();
                var idSet = new HashSet<int>(wordIds);
                var words = (await _database!.Table<Word>().ToListAsync())
                    .Where(w => idSet.Contains(w.Id))
                    .ToDictionary(w => w.Id, w => w.Text);

                var result = new List<TodayReviewItem>();
                foreach (var log in logs)
                {
                    result.Add(new TodayReviewItem
                    {
                        LogId = log.Id,
                        WordId = log.WordId,
                        WordText = words.TryGetValue(log.WordId, out var text) ? text : $"[未知词汇#{log.WordId}]",
                        Rating = log.Rating,
                        ReviewDate = log.ReviewDate
                    });
                }
                return result;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetTodayReviewItemsAsync");
                return new List<TodayReviewItem>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task ChangeTodayReviewRatingAsync(int logId, int newRating)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var log = await _database!.FindAsync<ReviewLog>(logId);
                if (log == null) return;

                int oldRating = log.Rating;
                if (oldRating == newRating) return;

                var word = await _database!.FindAsync<Word>(log.WordId);
                if (word != null)
                {
                    var allWordLogs = (await _database!.Table<ReviewLog>()
                        .Where(l => l.WordId == word.Id)
                        .ToListAsync())
                        .OrderBy(l => l.ReviewDate)
                        .ThenBy(l => l.Id)
                        .ToList();

                    var priorLogs = allWordLogs
                        .Where(l => l.Id != logId && (l.ReviewDate < log.ReviewDate || (l.ReviewDate == log.ReviewDate && l.Id < logId)))
                        .ToList();
                    var subsequentLogs = allWordLogs
                        .Where(l => l.Id != logId && (l.ReviewDate > log.ReviewDate || (l.ReviewDate == log.ReviewDate && l.Id > logId)))
                        .ToList();

                    var previousLog = priorLogs.LastOrDefault();
                    int preReps = priorLogs.Count;
                    int preLapses = priorLogs.Count(l => l.Rating == 1);

                    double preStability = 0.0;
                    double preDifficulty = 0.0;
                    DateTime? preLastReviewDate = null;

                    if (preReps > 0)
                    {
                        preStability = log.StabilityBefore > 0
                            ? log.StabilityBefore
                            : (previousLog != null && previousLog.StabilityAfter > 0 ? previousLog.StabilityAfter : word.Stability);
                        preDifficulty = log.DifficultyBefore > 0
                            ? log.DifficultyBefore
                            : (previousLog != null && previousLog.DifficultyAfter > 0 ? previousLog.DifficultyAfter : word.Difficulty);
                        preLastReviewDate = previousLog != null
                            ? previousLog.ReviewDate
                            : log.ReviewDate.AddDays(-Math.Max(1.0, preStability));
                    }

                    int preState = preReps == 0
                        ? (int)WordLearningState.New
                        : ((preReps < 2 || (previousLog != null && previousLog.Rating == 1))
                            ? (int)WordLearningState.Learning
                            : (int)WordLearningState.Review);

                    var simWord = new Word
                    {
                        Id = word.Id,
                        Text = word.Text,
                        CreatedAt = word.CreatedAt,
                        State = preState,
                        Stability = preStability,
                        Difficulty = preDifficulty,
                        Reps = preReps,
                        Lapses = preLapses,
                        LastReviewDate = preLastReviewDate
                    };

                    int elapsedDays = preReps == 0
                        ? 0
                        : (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(preLastReviewDate ?? word.CreatedAt, log.ReviewDate));
                    var (newS, newD, nextReview) = FsrsEngine.Review(simWord, newRating, log.ReviewDate);
                    int scheduledDays = Math.Max(1, (int)Math.Round((nextReview.Date - log.ReviewDate.Date).TotalDays));

                    log.Rating = newRating;
                    log.StabilityBefore = preStability;
                    log.DifficultyBefore = preDifficulty;
                    log.StabilityAfter = newS;
                    log.DifficultyAfter = newD;
                    log.State = preState;
                    log.ElapsedDays = elapsedDays;
                    log.ScheduledDays = scheduledDays;
                    await _database!.UpdateAsync(log);

                    simWord.Stability = newS;
                    simWord.Difficulty = newD;
                    simWord.LastReviewDate = log.ReviewDate;
                    simWord.NextReviewDate = nextReview;
                    simWord.Reps = preReps + 1;
                    simWord.Lapses = preLapses + (newRating == 1 ? 1 : 0);
                    simWord.State = (newRating == 1 || simWord.Reps < 2)
                        ? (int)WordLearningState.Learning
                        : (int)WordLearningState.Review;

                    foreach (var subLog in subsequentLogs)
                    {
                        int subPreState = simWord.State;
                        double subPreS = simWord.Stability;
                        double subPreD = simWord.Difficulty;
                        int subElapsed = (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(simWord.LastReviewDate ?? word.CreatedAt, subLog.ReviewDate));
                        var (subS, subD, subNext) = FsrsEngine.Review(simWord, subLog.Rating, subLog.ReviewDate);
                        int subSched = Math.Max(1, (int)Math.Round((subNext.Date - subLog.ReviewDate.Date).TotalDays));

                        subLog.StabilityBefore = subPreS;
                        subLog.DifficultyBefore = subPreD;
                        subLog.StabilityAfter = subS;
                        subLog.DifficultyAfter = subD;
                        subLog.State = subPreState;
                        subLog.ElapsedDays = subElapsed;
                        subLog.ScheduledDays = subSched;
                        await _database!.UpdateAsync(subLog);

                        simWord.Stability = subS;
                        simWord.Difficulty = subD;
                        simWord.LastReviewDate = subLog.ReviewDate;
                        simWord.NextReviewDate = subNext;
                        simWord.Reps += 1;
                        if (subLog.Rating == 1) simWord.Lapses += 1;
                        simWord.State = (subLog.Rating == 1 || simWord.Reps < 2)
                            ? (int)WordLearningState.Learning
                            : (int)WordLearningState.Review;
                    }

                    word.Stability = simWord.Stability;
                    word.Difficulty = simWord.Difficulty;
                    word.LastReviewDate = simWord.LastReviewDate;
                    word.Reps = simWord.Reps;
                    word.Lapses = simWord.Lapses;

                    if (word.State == (int)WordLearningState.Mastered)
                    {
                        // 【已掌握】为独立分类，改判复习仅更新底层 FSRS 科学参数，业务状态恒为 Mastered 且免排期
                        word.NextReviewDate = null;
                    }
                    else
                    {
                        word.State = simWord.State;
                        word.NextReviewDate = simWord.NextReviewDate;
                    }

                    word.StateUpdatedAt = DateTime.Now;
                    await _database!.UpdateAsync(word);
                }
                else
                {
                    log.Rating = newRating;
                    await _database!.UpdateAsync(log);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.ChangeTodayReviewRatingAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task RevertTodayReviewAsync(int logId, bool isSyncRevert = false)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var log = await _database!.FindAsync<ReviewLog>(logId);
                if (log == null) return;

                var word = await _database!.FindAsync<Word>(log.WordId);
                await _database!.DeleteAsync<ReviewLog>(logId);

                if (word != null)
                {
                    var remainingLogs = (await _database!.Table<ReviewLog>()
                        .Where(l => l.WordId == word.Id)
                        .ToListAsync())
                        .OrderBy(l => l.ReviewDate)
                        .ThenBy(l => l.Id)
                        .ToList();

                    if (remainingLogs.Count == 0)
                    {
                        word.Reps = 0;
                        word.Lapses = 0;
                        word.Stability = 0.0;
                        word.Difficulty = 0.0;
                        word.LastReviewDate = null;
                        word.NextReviewDate = null;
                        if (word.State != (int)WordLearningState.Mastered)
                        {
                            word.State = (int)WordLearningState.New;
                        }
                    }
                    else
                    {
                        // 从剩余日志重演或恢复到最后一条日志的准确状态
                        var simWord = new Word
                        {
                            Id = word.Id,
                            Text = word.Text,
                            CreatedAt = word.CreatedAt,
                            State = (int)WordLearningState.New,
                            Stability = 0.0,
                            Difficulty = 0.0,
                            Reps = 0,
                            Lapses = 0,
                            LastReviewDate = null
                        };

                        foreach (var rLog in remainingLogs)
                        {
                            int rPreState = simWord.State;
                            double rPreS = simWord.Stability;
                            double rPreD = simWord.Difficulty;
                            int rElapsed = simWord.Reps == 0
                                ? 0
                                : (int)Math.Round(FsrsEngine.GetCalendarElapsedDays(simWord.LastReviewDate ?? word.CreatedAt, rLog.ReviewDate));
                            var (rNewS, rNewD, rNext) = FsrsEngine.Review(simWord, rLog.Rating, rLog.ReviewDate);
                            int rSched = Math.Max(1, (int)Math.Round((rNext.Date - rLog.ReviewDate.Date).TotalDays));

                            if (Math.Abs(rLog.StabilityBefore - rPreS) > 0.001 || Math.Abs(rLog.StabilityAfter - rNewS) > 0.001)
                            {
                                rLog.StabilityBefore = rPreS;
                                rLog.DifficultyBefore = rPreD;
                                rLog.StabilityAfter = rNewS;
                                rLog.DifficultyAfter = rNewD;
                                rLog.State = rPreState;
                                rLog.ElapsedDays = rElapsed;
                                rLog.ScheduledDays = rSched;
                                await _database!.UpdateAsync(rLog);
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

                        word.Reps = simWord.Reps;
                        word.Lapses = simWord.Lapses;
                        word.Stability = simWord.Stability;
                        word.Difficulty = simWord.Difficulty;
                        word.LastReviewDate = simWord.LastReviewDate;

                        if (word.State == (int)WordLearningState.Mastered)
                        {
                            // 【已掌握】为独立分类，撤销复习后业务状态恒为 Mastered 且免排期
                            word.NextReviewDate = null;
                        }
                        else if (!isSyncRevert)
                        {
                            // 用户主动撤销今日复习：该词立即回到今日待复习队列，并恢复 FSRS 学习/复习状态
                            word.NextReviewDate = DateTime.Now;
                            word.State = simWord.State;
                        }
                        else
                        {
                            word.NextReviewDate = simWord.NextReviewDate;
                            word.State = simWord.State;
                        }
                    }

                    if (!isSyncRevert)
                    {
                        word.StateUpdatedAt = DateTime.Now;
                        DateTime localRev = log.ReviewDate.Kind == DateTimeKind.Utc ? log.ReviewDate.ToLocalTime() : log.ReviewDate;
                        string tombKey = $"{word.Text.Trim().ToLowerInvariant()}_{localRev:yyyyMMddHHmmss}";
                        await AddTombstoneInternalAsync("review_log", tombKey, DateTime.Now);
                    }
                    else if (word.StateUpdatedAt <= log.ReviewDate.AddSeconds(5))
                    {
                        word.StateUpdatedAt = word.LastReviewDate ?? word.CreatedAt;
                    }

                    await _database!.UpdateAsync(word);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.RevertTodayReviewAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task CleanupExpiredTombstonesAsync(int retentionDays = 90)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
                await _database!.Table<SyncTombstone>().DeleteAsync(t => t.DeletedAt < cutoff);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.CleanupExpiredTombstonesAsync");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task DeleteWordsAsync(IEnumerable<int> wordIds)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var idList = wordIds.Distinct().ToList();
                if (idList.Count == 0) return;

                var wordsToDelete = await _database!.Table<Word>()
                    .Where(w => idList.Contains(w.Id))
                    .ToListAsync();

                DateTime now = DateTime.Now;
                await _database!.RunInTransactionAsync(conn =>
                {
                    foreach (var w in wordsToDelete)
                    {
                        conn.Table<ReviewLog>().Delete(l => l.WordId == w.Id);
                        conn.Delete<Word>(w.Id);

                        string cleanKey = w.Text.Trim().ToLowerInvariant();
                        conn.Table<SyncTombstone>().Delete(t => t.EntityType == "word" && t.EntityKey == cleanKey);

                        // 记录单词永久删除墓碑
                        conn.Insert(new SyncTombstone
                        {
                            EntityType = "word",
                            EntityKey = cleanKey,
                            DeletedAt = now
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.DeleteWordsAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task<List<SyncTombstone>> GetAllTombstonesAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<SyncTombstone>().ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetAllTombstonesAsync");
                return new List<SyncTombstone>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        private async Task AddTombstoneInternalAsync(string entityType, string entityKey, DateTime? deletedAt = null)
        {
            string cleanKey = entityKey.Trim().ToLowerInvariant();
            DateTime effectiveDeletedAt = deletedAt != null && deletedAt.Value != default ? deletedAt.Value : DateTime.Now;
            var existing = await _database!.Table<SyncTombstone>()
                .Where(t => t.EntityType == entityType && t.EntityKey == cleanKey)
                .FirstOrDefaultAsync();

            if (existing == null)
            {
                await _database!.InsertAsync(new SyncTombstone
                {
                    EntityType = entityType,
                    EntityKey = cleanKey,
                    DeletedAt = effectiveDeletedAt
                });
            }
            else if (effectiveDeletedAt > existing.DeletedAt)
            {
                existing.DeletedAt = effectiveDeletedAt;
                await _database!.UpdateAsync(existing);
            }
        }

        private async Task RemoveTombstoneInternalAsync(string entityType, string entityKey)
        {
            string cleanKey = entityKey.Trim().ToLowerInvariant();
            var targets = await _database!.Table<SyncTombstone>()
                .Where(t => t.EntityType == entityType && t.EntityKey == cleanKey)
                .ToListAsync();
            foreach (var t in targets)
            {
                await _database!.DeleteAsync(t);
            }
        }

        public async Task AddTombstoneAsync(string entityType, string entityKey, DateTime? deletedAt = null)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                await AddTombstoneInternalAsync(entityType, entityKey, deletedAt);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.AddTombstoneAsync");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task RemoveTombstoneAsync(string entityType, string entityKey)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                await RemoveTombstoneInternalAsync(entityType, entityKey);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.RemoveTombstoneAsync");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task ReimportWordsTodayAsync(IEnumerable<int> wordIds)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var idSet = new HashSet<int>(wordIds);
                var words = (await _database!.Table<Word>().ToListAsync()).Where(w => idSet.Contains(w.Id)).ToList();
                DateTime now = DateTime.Now;

                // 科学且彻底的“重新在今天导入”：
                // 1. 删除旧单词对象与历史复习日志（并记录日志撤销墓碑），防止历史干扰与同步幽灵复活；
                // 2. 将其以当前时间戳作为全新的未学习单词对象重新入库。
                await _database!.RunInTransactionAsync(conn =>
                {
                    foreach (var w in words)
                    {
                        string text = w.Text;
                        string cleanWordKey = text.Trim().ToLowerInvariant();
                        int? listId = w.WordListId;
                        bool inList = w.IsInList;

                        var oldLogs = conn.Table<ReviewLog>().Where(l => l.WordId == w.Id).ToList();
                        foreach (var oldLog in oldLogs)
                        {
                            string logTombKey = $"{cleanWordKey}_{oldLog.ReviewDate:yyyyMMddHHmmss}";
                            conn.Table<SyncTombstone>().Delete(t => t.EntityType == "review_log" && t.EntityKey == logTombKey);
                            conn.Insert(new SyncTombstone
                            {
                                EntityType = "review_log",
                                EntityKey = logTombKey,
                                DeletedAt = now
                            });
                        }

                        // 删除旧记录与日志，同时清除可能存在的 word 墓碑
                        conn.Table<ReviewLog>().Delete(l => l.WordId == w.Id);
                        conn.Table<SyncTombstone>().Delete(t => t.EntityType == "word" && t.EntityKey == cleanWordKey);
                        conn.Delete<Word>(w.Id);

                        // 重新在今天导入全新单词对象
                        var newWord = new Word
                        {
                            Text = text,
                            CreatedAt = now,
                            StateUpdatedAt = now,
                            MetaUpdatedAt = now,
                            WordListId = listId,
                            IsInList = inList,
                            State = (int)WordLearningState.New,
                            Stability = 0.0,
                            Difficulty = 0.0,
                            Reps = 0,
                            Lapses = 0,
                            LastReviewDate = null,
                            NextReviewDate = null
                        };
                        conn.Insert(newWord);
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.ReimportWordsTodayAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        public async Task<List<Word>> GetRecentStudyWordsAsync(string timeSpanLevel, WordLearningState? stateFilter)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                DateTime now = DateTime.Now;
                DateTime startTime = timeSpanLevel switch
                {
                    "Day" => now.Date,                                 // 当天/24小时
                    "Week" => now.Date.AddDays(-7),                   // 最近7天
                    "Month" => now.Date.AddDays(-30),                 // 最近30天
                    "Quarter" => now.Date.AddDays(-90),               // 最近90天
                    _ => now.Date.AddDays(-7)
                };

                // 从 ReviewLog 中获取在指定时间区间内被复习过的 WordId 集合
                var recentLogs = await _database!.Table<ReviewLog>()
                    .Where(l => l.ReviewDate >= startTime)
                    .OrderByDescending(l => l.ReviewDate)
                    .ToListAsync();

                var recentWordIdSet = recentLogs.Select(l => l.WordId).Distinct().ToHashSet();

                var allWords = await _database!.Table<Word>().ToListAsync();
                var result = allWords.Where(w => recentWordIdSet.Contains(w.Id) || (w.LastReviewDate.HasValue && w.LastReviewDate.Value >= startTime)).ToList();

                if (stateFilter.HasValue)
                {
                    result = result.Where(w => w.State == (int)stateFilter.Value).ToList();
                }

                return result.OrderByDescending(w => w.LastReviewDate ?? w.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetRecentStudyWordsAsync");
                return new List<Word>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        /// <summary>
        /// 清空所有导入的单词、词单与复习记录（用户偏好设置危险区调用）
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                await _database!.DeleteAllAsync<Word>();
                await _database!.DeleteAllAsync<WordList>();
                await _database!.DeleteAllAsync<ReviewLog>();
                await _database!.DeleteAllAsync<SyncTombstone>();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.ClearAllDataAsync");
                throw;
            }
            finally
            {
                _dbLock.Release();
            }

            DataChanged?.Invoke();
        }

        #region 用户偏好与界面状态持久化 (UserPreferences)

        public async Task<List<UserPreference>> GetAllPreferencesAsync()
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                return await _database!.Table<UserPreference>().ToListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "DatabaseService.GetAllPreferencesAsync");
                return new List<UserPreference>();
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<string> GetPreferenceAsync(string key, string defaultValue = "")
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var pref = await _database!.Table<UserPreference>().Where(p => p.Key == key).FirstOrDefaultAsync();
                return pref?.Value ?? defaultValue;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"DatabaseService.GetPreferenceAsync({key})");
                return defaultValue;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task SetPreferenceAsync(string key, string value)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                await _database!.InsertOrReplaceAsync(new UserPreference { Key = key, Value = value });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"DatabaseService.SetPreferenceAsync({key})");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task RemovePreferenceAsync(string key)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                await _database!.Table<UserPreference>().DeleteAsync(p => p.Key == key);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"DatabaseService.RemovePreferenceAsync({key})");
            }
            finally
            {
                _dbLock.Release();
            }
        }

        #endregion

        #endregion
    }
}
