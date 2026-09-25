using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class AllWordsViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedStateFilter = "All";

        [ObservableProperty]
        private string _selectedDimension = "Month";

        [ObservableProperty]
        private bool _isFlatLevelMode = false;

        [ObservableProperty]
        private DateHierarchyNode? _selectedNode;

        [ObservableProperty]
        private bool _isTimeSliceActive;

        [ObservableProperty]
        private string _selectedNodeTitle = LocalizationService.Instance.GetString("AllTimePanoramicOverview", "全部时间（全量总览）");

        [ObservableProperty]
        private int _sliceTotalCount;

        [ObservableProperty]
        private int _sliceNewCount;

        [ObservableProperty]
        private int _sliceLearningCount;

        [ObservableProperty]
        private int _sliceReviewingCount;

        [ObservableProperty]
        private int _sliceMasteredCount;

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _newCount;

        [ObservableProperty]
        private int _learningCount;

        [ObservableProperty]
        private int _reviewingCount;

        [ObservableProperty]
        private int _masteredCount;

        [ObservableProperty]
        private int _unarchivedCount;

        [ObservableProperty]
        private int _archivedCount;

        [ObservableProperty]
        private WordList? _selectedWordList;

        [ObservableProperty]
        private string _bottomPanelMode = "TimeSlice";

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ObservableCollection<Word> AllWords { get; } = new();
        public ObservableCollection<Word> DisplayWords { get; } = new();
        public ObservableCollection<DateHierarchyNode> DateTreeNodes { get; } = new();
        public ObservableCollection<WordList> WordLists { get; } = new();
        public event Action? TreeRebuilt;

        private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);

        public string PageTitle => LocalizationService.Instance.GetString("NavAllWords", "词库");
        public string RefreshButtonText => LocalizationService.Instance.GetString("ButtonRefreshData", "刷新数据");
        public string TimeSliceTitle => LocalizationService.Instance.GetString("TimeSliceTitle", "时间节点切片");
        public string TimeSliceTip => LocalizationService.Instance.GetString("TimeSliceTip", "点击时间节点可筛选该区间导入的词汇");
        public string DimensionYearText => LocalizationService.Instance.GetString("DimensionYear", "按年分类");
        public string DimensionQuarterText => LocalizationService.Instance.GetString("DimensionQuarter", "按季分类");
        public string DimensionMonthText => LocalizationService.Instance.GetString("DimensionMonth", "按月分类");
        public string DimensionWeekText => LocalizationService.Instance.GetString("DimensionWeek", "按周分类");
        public string DimensionDayText => LocalizationService.Instance.GetString("DimensionDay", "按日分类");
        public string FlatLevelModeText => LocalizationService.Instance.GetString("FlatLevelMode", "仅显示当前层级（单层扁平模式）");
        public string TimeSliceSelectedLabel => LocalizationService.Instance.GetString("TimeSliceSelectedLabel", "时间切片筛选状态");
        public string SliceNewText => LocalizationService.Instance.GetString("SliceNew", "未学");
        public string SliceLearningText => LocalizationService.Instance.GetString("SliceLearning", "学习");
        public string SliceReviewingText => LocalizationService.Instance.GetString("SliceReviewing", "复习");
        public string SliceMasteredText => LocalizationService.Instance.GetString("SliceMastered", "掌握");
        public string ButtonPackSliceToListText => LocalizationService.Instance.GetString("ButtonPackSliceToList", "打包此切片为新词单");
        public string ButtonClearSliceText => LocalizationService.Instance.GetString("ButtonClearSlice", "清除时间切片，查看全部");
        public string FilterAllText => LocalizationService.Instance.GetString("FilterAll", "全部");
        public string FilterNewText => string.Format(LocalizationService.Instance.GetString("FilterNewFormat", "未学习 ({0})"), NewCount);
        public string FilterLearningText => string.Format(LocalizationService.Instance.GetString("FilterLearningFormat", "学习中 ({0})"), LearningCount);
        public string FilterReviewText => string.Format(LocalizationService.Instance.GetString("FilterReviewFormat", "复习中 ({0})"), ReviewingCount);
        public string FilterMasteredText => string.Format(LocalizationService.Instance.GetString("FilterMasteredFormat", "已掌握 ({0})"), MasteredCount);
        public string FilterUnarchivedText => string.Format(LocalizationService.Instance.GetString("FilterUnarchivedFormat", "未归档 ({0})"), UnarchivedCount);
        public string FilterArchivedText => string.Format(LocalizationService.Instance.GetString("FilterArchivedFormat", "已归档 ({0})"), ArchivedCount);
        public string CustomListsTitle => LocalizationService.Instance.GetString("CustomListsTitle", "自定义专属词单");
        public string CustomListsSubTitle => LocalizationService.Instance.GetString("CustomListsSubTitle", "选择词单可查看与管理词单词汇");
        public string TabTimeSliceText => LocalizationService.Instance.GetString("TabTimeSliceText", "时间切片");
        public string TabCustomListsText => LocalizationService.Instance.GetString("TabCustomListsText", "专属词单");
        public string SearchWordsPlaceholder => LocalizationService.Instance.GetString("SearchWordsPlaceholder", "输入单词查找...");
        public string SelectAllResultsText => LocalizationService.Instance.GetString("SelectAllResults", "全选当前结果");
        public string RightClickActionHint => LocalizationService.Instance.GetString("RightClickActionHint", "多选单词后右键已选词弹出操作菜单");
        public string ButtonCreateCustomListText => LocalizationService.Instance.GetString("ButtonCreateCustomList", "新建自选词单");
        public string NextReviewLabel => LocalizationService.Instance.GetString("NextReviewLabel", "下次复习");

        partial void OnNewCountChanged(int value) => OnPropertyChanged(nameof(FilterNewText));
        partial void OnLearningCountChanged(int value) => OnPropertyChanged(nameof(FilterLearningText));
        partial void OnReviewingCountChanged(int value) => OnPropertyChanged(nameof(FilterReviewText));
        partial void OnMasteredCountChanged(int value) => OnPropertyChanged(nameof(FilterMasteredText));
        partial void OnUnarchivedCountChanged(int value) => OnPropertyChanged(nameof(FilterUnarchivedText));
        partial void OnArchivedCountChanged(int value) => OnPropertyChanged(nameof(FilterArchivedText));

        public AllWordsViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;

            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                if (SelectedNode == null)
                {
                    SelectedNodeTitle = LocalizationService.Instance.GetString("AllTimePanoramicOverview", "全部时间（全量总览）");
                }
                BuildDateTree();
                FilterWords();
                OnPropertyChanged(string.Empty);
            };
        }

        public async Task LoadDataAsync()
        {
            await _loadLock.WaitAsync();
            try
            {
                var words = await _databaseService.GetAllWordsAsync();
                AllWords.Clear();
                foreach (var w in words)
                {
                    AllWords.Add(w);
                }

                TotalCount = words.Count;
                NewCount = words.Count(w => w.State == (int)WordLearningState.New);
                LearningCount = words.Count(w => w.State == (int)WordLearningState.Learning);
                ReviewingCount = words.Count(w => w.State == (int)WordLearningState.Review);
                MasteredCount = words.Count(w => w.State == (int)WordLearningState.Mastered);
                UnarchivedCount = words.Count(w => !w.IsInList);
                ArchivedCount = words.Count(w => w.IsInList);

                var lists = await _databaseService.GetAllWordListsAsync();
                WordLists.Clear();
                foreach (var l in lists)
                {
                    WordLists.Add(l);
                }
                if (SelectedWordList != null)
                {
                    SelectedWordList = WordLists.FirstOrDefault(l => l.Id == SelectedWordList.Id);
                }

                var prefFlat = await _databaseService.GetPreferenceAsync("AllWords_IsFlatLevelMode", "false");
                if (bool.TryParse(prefFlat, out var isFlat) && isFlat != IsFlatLevelMode)
                {
                    IsFlatLevelMode = isFlat;
                }

                BuildDateTree();
                UpdateSliceStatistics();
                FilterWords();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsViewModel.LoadDataAsync");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        partial void OnSearchTextChanged(string value) => FilterWords();
        partial void OnSelectedStateFilterChanged(string value) => FilterWords();
        partial void OnSelectedDimensionChanged(string value) => BuildDateTree();
        partial void OnIsFlatLevelModeChanged(bool value)
        {
            _ = _databaseService.SetPreferenceAsync("AllWords_IsFlatLevelMode", value.ToString());
            BuildDateTree();
        }

        public void SelectNode(DateHierarchyNode? node)
        {
            SelectedNode = node;
            IsTimeSliceActive = node != null;
            if (node != null)
            {
                SelectedWordList = null;
            }
            SelectedNodeTitle = node != null ? node.Title : LocalizationService.Instance.GetString("AllTimePanoramicOverview", "全部时间（全量总览）");
            UpdateSliceStatistics();
            FilterWords();
        }

        public void SelectWordList(WordList? list)
        {
            SelectedWordList = list;
            if (list != null)
            {
                SelectedNode = null;
                IsTimeSliceActive = false;
                SelectedNodeTitle = string.Format(LocalizationService.Instance.GetString("CustomWordListActiveTitleFormat", "专属词单：{0}"), list.Name);
            }
            else
            {
                SelectedNodeTitle = LocalizationService.Instance.GetString("AllTimePanoramicOverview", "全部时间（全量总览）");
            }
            UpdateSliceStatistics();
            FilterWords();
        }

        public async Task RenameWordListAsync(WordList list, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            await _databaseService.RenameWordListAsync(list.Id, newName);
            StatusMessage = $"词单已重命名为「{newName.Trim()}」";
            await LoadDataAsync();
        }

        public async Task MergeWordListsAsync(WordList source, WordList target)
        {
            await _databaseService.MergeWordListsAsync(source.Id, target.Id);
            StatusMessage = $"已将词单「{source.Name}」合并入「{target.Name}」";
            await LoadDataAsync();
        }

        public async Task DeleteWordListAsync(WordList list)
        {
            if (SelectedWordList?.Id == list.Id)
            {
                SelectedWordList = null;
            }
            await _databaseService.DeleteWordListAsync(list.Id);
            StatusMessage = string.Format(LocalizationService.Instance.GetString("MsgWordListDisbanded", "已解散词单「{0}」，单词已恢复为未归档状态。"), list.Name);
            await LoadDataAsync();
        }

        public async Task ExportWordListToWebAsync(WordList list)
        {
            try
            {
                var words = await _databaseService.GetWordsByListIdAsync(list.Id);
                var webExport = App.GetService<WebExportService>();
                await webExport.OpenInBrowserAsync(words);
                StatusMessage = "网页词库已成功导出并在系统默认浏览器中打开！";
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "AllWordsViewModel.ExportWordListToWebAsync");
                StatusMessage = $"导出失败：{ex.Message}";
            }
        }

        private void UpdateSliceStatistics()
        {
            if (SelectedWordList != null)
            {
                var listWords = AllWords.Where(w => w.WordListId == SelectedWordList.Id).ToList();
                SliceTotalCount = listWords.Count;
                SliceNewCount = listWords.Count(w => w.State == (int)WordLearningState.New);
                SliceLearningCount = listWords.Count(w => w.State == (int)WordLearningState.Learning);
                SliceReviewingCount = listWords.Count(w => w.State == (int)WordLearningState.Review);
                SliceMasteredCount = listWords.Count(w => w.State == (int)WordLearningState.Mastered);
            }
            else if (SelectedNode != null)
            {
                var targetIds = new HashSet<int>(SelectedNode.GetAllContainedWordIds());
                var sliceWords = AllWords.Where(w => targetIds.Contains(w.Id)).ToList();
                SliceTotalCount = sliceWords.Count;
                SliceNewCount = sliceWords.Count(w => w.State == (int)WordLearningState.New);
                SliceLearningCount = sliceWords.Count(w => w.State == (int)WordLearningState.Learning);
                SliceReviewingCount = sliceWords.Count(w => w.State == (int)WordLearningState.Review);
                SliceMasteredCount = sliceWords.Count(w => w.State == (int)WordLearningState.Mastered);
            }
            else
            {
                SliceTotalCount = TotalCount;
                SliceNewCount = NewCount;
                SliceLearningCount = LearningCount;
                SliceReviewingCount = ReviewingCount;
                SliceMasteredCount = MasteredCount;
            }
        }

        public void FilterWords()
        {
            DisplayWords.Clear();
            var query = AllWords.AsEnumerable();

            // 1. 状态与归档分类过滤
            if (SelectedStateFilter == "Unarchived")
            {
                query = query.Where(w => !w.IsInList);
            }
            else if (SelectedStateFilter == "Archived")
            {
                query = query.Where(w => w.IsInList);
            }
            else if (SelectedStateFilter != "All" && Enum.TryParse<WordLearningState>(SelectedStateFilter, out var state))
            {
                query = query.Where(w => w.State == (int)state);
            }

            // 2. 专属词单过滤
            if (SelectedWordList != null)
            {
                query = query.Where(w => w.WordListId == SelectedWordList.Id);
            }
            // 3. 时间节点切片过滤（仅在未选择专属词单时）
            else if (SelectedNode != null)
            {
                var targetIds = new HashSet<int>(SelectedNode.GetAllContainedWordIds());
                query = query.Where(w => targetIds.Contains(w.Id));
            }

            // 4. 搜索词过滤（Zero-Definition纯单词哲学）
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(w => w.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var w in query)
            {
                DisplayWords.Add(w);
            }
        }

        public void BuildDateTree()
        {
            DateTreeNodes.Clear();
            if (AllWords.Count == 0) return;

            var calendar = CultureInfo.CurrentCulture.Calendar;
            var wordList = AllWords.ToList();
            var loc = LocalizationService.Instance;

            // 1. 单层扁平模式：直接平铺展示该维度对象
            if (IsFlatLevelMode)
            {
                if (SelectedDimension == "Year")
                {
                    var yg = wordList.GroupBy(w => w.CreatedAt.Year).OrderByDescending(g => g.Key);
                    foreach (var g in yg)
                    {
                        var node = new DateHierarchyNode { Title = string.Format(loc.GetString("DateTreeNodeYearFormat", "{0} 年 ({1} 词)"), g.Key, g.Count()) };
                        node.WordIds.AddRange(g.Select(w => w.Id));
                        DateTreeNodes.Add(node);
                    }
                }
                else if (SelectedDimension == "Quarter")
                {
                    var qg = wordList.GroupBy(w => new { Year = w.CreatedAt.Year, Quarter = (w.CreatedAt.Month - 1) / 3 + 1 })
                                     .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Quarter);
                    foreach (var g in qg)
                    {
                        var node = new DateHierarchyNode { Title = string.Format(loc.GetString("DateTreeNodeQuarterFormat", "{0} 年第 {1} 季度 ({2} 词)"), g.Key.Year, g.Key.Quarter, g.Count()) };
                        node.WordIds.AddRange(g.Select(w => w.Id));
                        DateTreeNodes.Add(node);
                    }
                }
                else if (SelectedDimension == "Month")
                {
                    var mg = wordList.GroupBy(w => new { Year = w.CreatedAt.Year, Month = w.CreatedAt.Month })
                                     .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month);
                    foreach (var g in mg)
                    {
                        var node = new DateHierarchyNode { Title = string.Format(loc.GetString("DateTreeNodeMonthFormat", "{0} 年 {1:D2} 月 ({2} 词)"), g.Key.Year, g.Key.Month, g.Count()) };
                        node.WordIds.AddRange(g.Select(w => w.Id));
                        DateTreeNodes.Add(node);
                    }
                }
                else if (SelectedDimension == "Week")
                {
                    var wg = wordList.GroupBy(w => new { Year = w.CreatedAt.Year, Week = calendar.GetWeekOfYear(w.CreatedAt, CalendarWeekRule.FirstDay, DayOfWeek.Monday) })
                                     .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Week);
                    foreach (var g in wg)
                    {
                        var node = new DateHierarchyNode { Title = string.Format(loc.GetString("DateTreeNodeWeekFormat", "{0} 年第 {1} 周 ({2} 词)"), g.Key.Year, g.Key.Week, g.Count()) };
                        node.WordIds.AddRange(g.Select(w => w.Id));
                        DateTreeNodes.Add(node);
                    }
                }
                else if (SelectedDimension == "Day")
                {
                    var dg = wordList.GroupBy(w => w.CreatedAt.Date).OrderByDescending(g => g.Key);
                    foreach (var g in dg)
                    {
                        var node = new DateHierarchyNode { Title = string.Format(loc.GetString("DateTreeNodeDayFormat", "{0:yyyy-MM-dd} ({1} 词)"), g.Key, g.Count()) };
                        node.WordIds.AddRange(g.Select(w => w.Id));
                        DateTreeNodes.Add(node);
                    }
                }
                TreeRebuilt?.Invoke();
                return;
            }

            // 2. 传统层级折叠树
            var yearGroups = AllWords.GroupBy(w => w.CreatedAt.Year).OrderByDescending(g => g.Key);

            foreach (var yg in yearGroups)
            {
                var yearNode = new DateHierarchyNode
                {
                    Title = string.Format(loc.GetString("DateTreeNodeYearFormat", "{0} 年 ({1} 词)"), yg.Key, yg.Count())
                };
                yearNode.WordIds.AddRange(yg.Select(w => w.Id));

                if (SelectedDimension == "Year")
                {
                    DateTreeNodes.Add(yearNode);
                    continue;
                }

                if (SelectedDimension == "Quarter")
                {
                    var quarterGroups = yg.GroupBy(w => (w.CreatedAt.Month - 1) / 3 + 1).OrderByDescending(g => g.Key);
                    foreach (var qg in quarterGroups)
                    {
                        var quarterNode = new DateHierarchyNode
                        {
                            Title = string.Format(loc.GetString("DateTreeNodeQuarterSubFormat", "第 {0} 季度 ({1} 词)"), qg.Key, qg.Count()),
                            Parent = yearNode
                        };
                        quarterNode.WordIds.AddRange(qg.Select(w => w.Id));

                        var monthGroups = qg.GroupBy(w => w.CreatedAt.Month).OrderByDescending(g => g.Key);
                        foreach (var mg in monthGroups)
                        {
                            var monthNode = new DateHierarchyNode
                            {
                                Title = string.Format(loc.GetString("DateTreeNodeMonthSubFormat", "{0} 月 ({1} 词)"), mg.Key, mg.Count()),
                                Parent = quarterNode
                            };
                            monthNode.WordIds.AddRange(mg.Select(w => w.Id));
                            quarterNode.Children.Add(monthNode);
                        }
                        yearNode.Children.Add(quarterNode);
                    }
                }
                else if (SelectedDimension == "Week")
                {
                    var weekGroups = yg.GroupBy(w => calendar.GetWeekOfYear(w.CreatedAt, CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                                       .OrderByDescending(g => g.Key);
                    foreach (var wg in weekGroups)
                    {
                        var weekNode = new DateHierarchyNode
                        {
                            Title = string.Format(loc.GetString("DateTreeNodeWeekSubFormat", "第 {0} 周 ({1} 词)"), wg.Key, wg.Count()),
                            Parent = yearNode
                        };
                        weekNode.WordIds.AddRange(wg.Select(w => w.Id));
                        yearNode.Children.Add(weekNode);
                    }
                }
                else if (SelectedDimension == "Day")
                {
                    var dayGroups = yg.GroupBy(w => w.CreatedAt.Date).OrderByDescending(g => g.Key);
                    foreach (var dg in dayGroups)
                    {
                        var dayNode = new DateHierarchyNode
                        {
                            Title = string.Format(loc.GetString("DateTreeNodeDayFormat", "{0:yyyy-MM-dd} ({1} 词)"), dg.Key, dg.Count()),
                            Parent = yearNode
                        };
                        dayNode.WordIds.AddRange(dg.Select(w => w.Id));
                        yearNode.Children.Add(dayNode);
                    }
                }
                else // Month
                {
                    var monthGroups = yg.GroupBy(w => w.CreatedAt.Month).OrderByDescending(g => g.Key);
                    foreach (var mg in monthGroups)
                    {
                        var monthNode = new DateHierarchyNode
                        {
                            Title = string.Format(loc.GetString("DateTreeNodeMonthSubFormat", "{0} 月 ({1} 词)"), mg.Key, mg.Count()),
                            Parent = yearNode
                        };
                        monthNode.WordIds.AddRange(mg.Select(w => w.Id));

                        var dayGroups = mg.GroupBy(w => w.CreatedAt.Date).OrderByDescending(g => g.Key);
                        foreach (var dg in dayGroups)
                        {
                            var dayNode = new DateHierarchyNode
                            {
                                Title = string.Format(loc.GetString("DateTreeNodeDayFormat", "{0:yyyy-MM-dd} ({1} 词)"), dg.Key, dg.Count()),
                                Parent = monthNode
                            };
                            dayNode.WordIds.AddRange(dg.Select(w => w.Id));
                            monthNode.Children.Add(dayNode);
                        }
                        yearNode.Children.Add(monthNode);
                    }
                }

                DateTreeNodes.Add(yearNode);
            }

            // 默认展开顶层节点，确保用户进入时时间节点切片内容一目了然
            foreach (var node in DateTreeNodes)
            {
                node.IsExpanded = true;
            }
            TreeRebuilt?.Invoke();
        }

        public void ExpandAll()
        {
            foreach (var node in DateTreeNodes)
            {
                node.SetExpandedRecursive(true);
            }
        }

        public void CollapseAll()
        {
            foreach (var node in DateTreeNodes)
            {
                node.SetExpandedRecursive(false);
            }
        }

        public async Task BatchUpdateStateAsync(IEnumerable<Word> words, WordLearningState newState)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            var loc = LocalizationService.Instance;
            await _databaseService.UpdateWordsStateAsync(list.Select(w => w.Id), newState);
            StatusMessage = string.Format(loc.GetString("MsgBatchUpdateStateSuccessFormat", "已将 {0} 个单词状态变更为 {1}"), list.Count, newState);
            await LoadDataAsync();
        }

        public async Task DeleteWordsAsync(IEnumerable<Word> words)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            var loc = LocalizationService.Instance;
            await _databaseService.DeleteWordsAsync(list.Select(w => w.Id));
            StatusMessage = string.Format(loc.GetString("MsgBatchDeleteWordsSuccessFormat", "已成功删除 {0} 个单词"), list.Count);
            await LoadDataAsync();
        }

        public async Task ReimportWordsTodayAsync(IEnumerable<Word> words)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            var loc = LocalizationService.Instance;
            await _databaseService.ReimportWordsTodayAsync(list.Select(w => w.Id));
            StatusMessage = string.Format(loc.GetString("MsgReimportWordsTodaySuccessFormat", "已将 {0} 个单词重置并在今天重新导入"), list.Count);
            SelectedNode = null;
            await LoadDataAsync();
        }

        public async Task<WordList?> CreateWordListFromWordsAsync(string name, IEnumerable<int> wordIds)
        {
            var ids = wordIds.ToList();
            if (ids.Count == 0) return null;

            var loc = LocalizationService.Instance;
            string defaultPrefix = loc.GetString("DefaultCustomListNamePrefix", "自选词单_");
            string listName = string.IsNullOrWhiteSpace(name)
                ? $"{defaultPrefix}{DateTime.Now:MMdd_HHmmss}"
                : name.Trim();

            var list = await _databaseService.CreateWordListAsync(listName, ids);
            StatusMessage = string.Format(loc.GetString("DialogMsgWordListCreatedSuccessFormat", "已成功创建词单「{0}」，共包含 {1} 个单词。"), list.Name, ids.Count);
            await LoadDataAsync();
            return list;
        }

        public async Task<WordList?> PackCurrentSliceToListAsync(string? customName = null)
        {
            if (SelectedNode == null) return null;
            var ids = SelectedNode.GetAllContainedWordIds().ToList();
            if (ids.Count == 0) return null;

            var loc = LocalizationService.Instance;
            string defaultPrefix = loc.GetString("DefaultSliceListNamePrefix", "切片词单_");
            string listName = string.IsNullOrWhiteSpace(customName)
                ? $"{defaultPrefix}{DateTime.Now:MMdd_HHmmss}"
                : customName.Trim();

            return await CreateWordListFromWordsAsync(listName, ids);
        }
    }
}
