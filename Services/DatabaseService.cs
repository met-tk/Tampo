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

        public DatabaseService()
        {
            SQLitePCL.Batteries_V2.Init();

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NihongoVocab");

            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            _dbPath = Path.Combine(appDataDir, "vocab.db");
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
                var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                foreach (var w in words)
                {
                    w.IsInList = false;
                    w.WordListId = null;
                    w.WordListName = null;
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
                var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                foreach (var w in words)
                {
                    await _database.Table<ReviewLog>().Where(r => r.WordId == w.Id).DeleteAsync();
                    await _database.DeleteAsync(w);
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
                    list.Name = newName.Trim();
                    await _database!.UpdateAsync(list);

                    var words = await _database!.Table<Word>().Where(w => w.WordListId == listId).ToListAsync();
                    foreach (var w in words)
                    {
                        w.WordListName = list.Name;
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

                var sourceWords = await _database!.Table<Word>().Where(w => w.WordListId == sourceListId).ToListAsync();
                foreach (var w in sourceWords)
                {
                    w.WordListId = targetListId;
                    w.WordListName = targetList.Name;
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
                    w.State = (int)newState;
                    if (newState == WordLearningState.Mastered)
                    {
                        w.Stability = Math.Max(w.Stability, 25.0);
                        w.LastReviewDate = now;
                        w.NextReviewDate = now.AddDays(25);
                    }
                    else if (newState == WordLearningState.New)
                    {
                        w.Stability = 0;
                        w.Difficulty = 0;
                        w.Reps = 0;
                        w.Lapses = 0;
                        w.NextReviewDate = null;
                    }
                    else if (newState == WordLearningState.Learning)
                    {
                        w.Stability = Math.Max(w.Stability, 1.0);
                        w.NextReviewDate = now;
                    }
                    else if (newState == WordLearningState.Review)
                    {
                        w.Stability = Math.Max(w.Stability, 5.0);
                        w.NextReviewDate = now;
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
                DateTime now = DateTime.Now;
                DateTime today = DateTime.Today;

                // 待学习/复习队列规则：
                // 1. 从未学习过的新词（Reps == 0）
                // 2. 到期需要复习的词（NextReviewDate <= now），并且严格排除今日已经复习过的词（LastReviewDate?.Date == today），彻底杜绝一天之内重复学习！
                var queue = allCandidates
                    .Where(w => w.Reps == 0 || (w.NextReviewDate.HasValue && w.NextReviewDate.Value <= now && (!w.LastReviewDate.HasValue || w.LastReviewDate.Value.Date < today)))
                    .OrderBy(w => w.NextReviewDate ?? DateTime.MinValue)
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
                double oldS = word.Stability;
                double oldD = word.Difficulty;

                var (newS, newD, nextReview) = FsrsEngine.Review(word, rating, now);

                word.Stability = newS;
                word.Difficulty = newD;
                word.LastReviewDate = now;
                word.NextReviewDate = nextReview;
                bool isFirstTime = word.Reps == 0;
                word.Reps += 1;

                if (rating == 1)
                {
                    word.Lapses += 1;
                    word.State = (int)WordLearningState.Learning;
                }
                else
                {
                    // 科学 SRS 哲学：
                    // 1. 新词首次学习记得，进入初期记忆建立期：Learning（学习中）
                    // 2. 只有在经过巩固复习后（Reps >= 2 且 rating == 3），才正式晋升为 Review（复习中）长期记忆周期
                    // 3. 已掌握（Mastered）严格由用户手动赋予，算法绝不自动归类
                    if (word.State == (int)WordLearningState.Mastered)
                    {
                        // 用户已经手动指定为已掌握，保持已掌握
                    }
                    else if (isFirstTime || word.Reps < 2)
                    {
                        word.State = (int)WordLearningState.Learning;
                    }
                    else
                    {
                        word.State = (int)WordLearningState.Review;
                    }
                }

                await _database!.UpdateAsync(word);

                var log = new ReviewLog
                {
                    WordId = word.Id,
                    Rating = rating,
                    ReviewDate = now,
                    StabilityBefore = oldS,
                    StabilityAfter = newS,
                    DifficultyBefore = oldD,
                    DifficultyAfter = newD
                };
                await _database!.InsertAsync(log);
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

        public async Task<WordList> CreateWordListAsync(string name, IEnumerable<int> wordIds)
        {
            await EnsureInitializedAsync();
            var idList = wordIds.ToList();

            await _dbLock.WaitAsync();
            try
            {
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
                DateTime now = DateTime.Now;

                foreach (var l in lists)
                {
                    var wordsInThis = allWords.Where(w => w.WordListId == l.Id).ToList();
                    l.WordCount = wordsInThis.Count;
                    l.UnreviewedCount = wordsInThis.Count(w => w.Reps == 0 || (w.NextReviewDate.HasValue && w.NextReviewDate.Value <= now));
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

                log.Rating = newRating;
                await _database!.UpdateAsync(log);

                var word = await _database!.FindAsync<Word>(log.WordId);
                if (word != null)
                {
                    DateTime now = DateTime.Now;
                    if (newRating == 3) // 改为记得
                    {
                        if (word.State != (int)WordLearningState.Mastered)
                        {
                            word.State = (word.Reps < 2) ? (int)WordLearningState.Learning : (int)WordLearningState.Review;
                        }
                        word.Lapses = Math.Max(0, word.Lapses - 1);
                        word.Stability = Math.Max(word.Stability, 3.0);
                        word.NextReviewDate = now.AddDays(Math.Max(1, (int)word.Stability));
                    }
                    else if (newRating == 1) // 改为遗忘
                    {
                        word.State = (int)WordLearningState.Learning;
                        word.Lapses += 1;
                        word.Stability = 0.5;
                        word.NextReviewDate = now;
                    }
                    await _database!.UpdateAsync(word);
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

        public async Task RevertTodayReviewAsync(int logId)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var log = await _database!.FindAsync<ReviewLog>(logId);
                if (log == null) return;

                var word = await _database!.FindAsync<Word>(log.WordId);
                if (word != null)
                {
                    word.Reps = Math.Max(0, word.Reps - 1);
                    if (word.Reps == 0)
                    {
                        word.State = (int)WordLearningState.New;
                        word.Stability = 0.0;
                        word.Difficulty = 0.0;
                        word.LastReviewDate = null;
                        word.NextReviewDate = null;
                    }
                    await _database!.UpdateAsync(word);
                }

                await _database!.DeleteAsync<ReviewLog>(logId);
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

        public async Task DeleteWordsAsync(IEnumerable<int> wordIds)
        {
            await EnsureInitializedAsync();
            await _dbLock.WaitAsync();
            try
            {
                var idList = wordIds.Distinct().ToList();
                if (idList.Count == 0) return;

                await _database!.RunInTransactionAsync(conn =>
                {
                    foreach (var id in idList)
                    {
                        conn.Table<ReviewLog>().Delete(l => l.WordId == id);
                        conn.Delete<Word>(id);
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
                // 1. 删除旧单词对象与历史复习日志，防止历史干扰与幽灵重复；
                // 2. 将其以当前时间戳作为全新的未学习单词对象重新入库。
                await _database!.RunInTransactionAsync(conn =>
                {
                    foreach (var w in words)
                    {
                        string text = w.Text;
                        int? listId = w.WordListId;
                        bool inList = w.IsInList;

                        // 删除旧记录与日志
                        conn.Table<ReviewLog>().Delete(l => l.WordId == w.Id);
                        conn.Delete<Word>(w.Id);

                        // 重新在今天导入全新单词对象
                        var newWord = new Word
                        {
                            Text = text,
                            CreatedAt = now,
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
