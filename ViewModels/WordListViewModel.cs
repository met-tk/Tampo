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
    public partial class WordListViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;
        private readonly WebExportService _webExportService;
        private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);
        private readonly System.Threading.SemaphoreSlim _createLock = new(1, 1);
        private DateHierarchyNode? _selectedDateNode;

        [ObservableProperty]
        private string _newListName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsCustomListSelected))]
        private WordList? _selectedWordList;

        public bool IsCustomListSelected => SelectedWordList != null;

        private bool _isCreatingList = false;
        public bool IsCreatingList
        {
            get => _isCreatingList;
            private set => SetProperty(ref _isCreatingList, value);
        }

        [ObservableProperty]
        private string _currentViewTitle = "请在左侧选择时间层级或词单";

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _selectedDimension = "Month"; // Year, Quarter, Month, Week, Day

        [ObservableProperty]
        private bool _isFlatLevelMode = false;

        public ObservableCollection<WordList> WordLists { get; } = new();
        public ObservableCollection<DateHierarchyNode> DateTreeNodes { get; } = new();
        public ObservableCollection<Word> ActiveWords { get; } = new();
        public ObservableCollection<Word> AllWords { get; } = new();
        public event Action? TreeRebuilt;

        public string PageTitle => LocalizationService.Instance.GetString("NavWordLists", "归档");
        public string ExportWebButtonText => LocalizationService.Instance.GetString("ExportCurrentSetWeb", "在浏览器中查看此集合");
        public string RefreshButtonText => LocalizationService.Instance.GetString("ButtonRefresh", "刷新");
        public string TreeTitle => LocalizationService.Instance.GetString("WordListTreeTitle", "时间维度折叠树");
        public string TreeTip => LocalizationService.Instance.GetString("WordListTreeTip", "点击节点即可在右侧预览其单词；勾选复选框可组合打包");
        public string DimensionYearText => LocalizationService.Instance.GetString("DimensionYear", "按年分类");
        public string DimensionQuarterText => LocalizationService.Instance.GetString("DimensionQuarter", "按季分类");
        public string DimensionMonthText => LocalizationService.Instance.GetString("DimensionMonth", "按月分类");
        public string DimensionWeekText => LocalizationService.Instance.GetString("DimensionWeek", "按周分类");
        public string DimensionDayText => LocalizationService.Instance.GetString("DimensionDay", "按日分类");
        public string FlatLevelModeText => LocalizationService.Instance.GetString("FlatLevelMode", "仅显示当前层级（单层扁平模式）");
        public string TreeListNameInputPlaceholder => LocalizationService.Instance.GetString("TreeListNameInputPlaceholder", "输入打包后的新词单名称...");
        public string ButtonPackSelection => LocalizationService.Instance.GetString("ButtonPackSelection", "将所选勾选项打包为新词单");
        public string CustomListsTitle => LocalizationService.Instance.GetString("CustomListsTitle", "自定义专属词单");
        public string CustomListsSubTitle => LocalizationService.Instance.GetString("CustomListsSubTitle", "右键可重命名、合并、解散或导出");
        public string SearchInCollectionPlaceholder => LocalizationService.Instance.GetString("SearchInCollectionPlaceholder", "在当前集合中查找单词...");
        public string SelectAllText => LocalizationService.Instance.GetString("SelectAll", "全选当前列表");
        public string ButtonCreateCustomListText => LocalizationService.Instance.GetString("ButtonCreateCustomList", "新建自选词单");
        public string ButtonMoveToListText => LocalizationService.Instance.GetString("ButtonMoveToList", "移动到词单...");
        public string RemoveFromListText => LocalizationService.Instance.GetString("RemoveFromList", "移出词单");
        public string NextReviewLabel => LocalizationService.Instance.GetString("NextReviewLabel", "下次复习");

        public WordListViewModel(DatabaseService databaseService, WebExportService webExportService)
        {
            _databaseService = databaseService;
            _webExportService = webExportService;

            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                BuildHierarchyTree(AllWords.Where(w => !w.IsInList));
                UpdateCurrentViewTitle();
                OnPropertyChanged(string.Empty);
            };
        }

        public void UpdateCurrentViewTitle()
        {
            var loc = LocalizationService.Instance;
            if (SelectedWordList != null)
            {
                CurrentViewTitle = string.Format(loc.GetString("CurrentViewTitleListFormat", "词单：{0}"), SelectedWordList.Name);
            }
            else if (_selectedDateNode != null)
            {
                CurrentViewTitle = string.Format(loc.GetString("CurrentViewTitleTimeFormat", "时间集合：{0}"), _selectedDateNode.Title);
            }
            else
            {
                CurrentViewTitle = loc.GetString("CurrentViewTitleAll", "词库未归档词汇全景");
            }
        }

        public async Task LoadDataAsync()
        {
            await _loadLock.WaitAsync();
            try
            {
                var lists = await _databaseService.GetAllWordListsAsync();
                var words = await _databaseService.GetAllWordsAsync();

                WordLists.Clear();
                foreach (var l in lists)
                {
                    WordLists.Add(l);
                }

                AllWords.Clear();
                foreach (var w in words)
                {
                    AllWords.Add(w);
                }

                BuildHierarchyTree(words.Where(w => !w.IsInList));

                if (SelectedWordList != null)
                {
                    var updated = WordLists.FirstOrDefault(l => l.Id == SelectedWordList.Id);
                    if (updated != null)
                    {
                        SelectedWordList = updated;
                        UpdateCurrentViewTitle();
                        var listWords = await _databaseService.GetWordsByListIdAsync(updated.Id);
                        ActiveWords.Clear();
                        foreach (var w in listWords)
                        {
                            ActiveWords.Add(w);
                        }
                    }
                    else
                    {
                        SelectedWordList = null;
                        ActiveWords.Clear();
                        CurrentViewTitle = LocalizationService.Instance.GetString("CurrentViewTitleNone", "当前未选择任何词单或集合");
                    }
                }
                else if (_selectedDateNode != null)
                {
                    // 保持用户当前查看的时间节点，绝对不自动切换到自定义词单
                    var ids = _selectedDateNode.GetAllContainedWordIds().ToList();
                    var nodeWords = await _databaseService.GetWordsByIdsAsync(ids);
                    UpdateCurrentViewTitle();
                    ActiveWords.Clear();
                    foreach (var w in nodeWords)
                    {
                        ActiveWords.Add(w);
                    }
                }
                else
                {
                    UpdateCurrentViewTitle();
                    ActiveWords.Clear();
                    foreach (var w in words.Where(w => !w.IsInList))
                    {
                        ActiveWords.Add(w);
                    }
                }

                var prefFlat = await _databaseService.GetPreferenceAsync("WordList_IsFlatLevelMode", "false");
                if (bool.TryParse(prefFlat, out var isFlat) && isFlat != IsFlatLevelMode)
                {
                    IsFlatLevelMode = isFlat;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListViewModel.LoadDataAsync");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        partial void OnSelectedDimensionChanged(string value)
        {
            BuildHierarchyTree(AllWords.Where(w => !w.IsInList));
        }

        partial void OnIsFlatLevelModeChanged(bool value)
        {
            _ = _databaseService.SetPreferenceAsync("WordList_IsFlatLevelMode", value.ToString());
            BuildHierarchyTree(AllWords.Where(w => !w.IsInList));
        }

        public void BuildHierarchyTree(IEnumerable<Word> unlistedWords)
        {
            DateTreeNodes.Clear();
            var calendar = CultureInfo.CurrentCulture.Calendar;
            var wordList = unlistedWords.ToList();
            var loc = LocalizationService.Instance;
            if (wordList.Count == 0) return;

            // 1. 单层扁平模式：只显示该时间层级下的对象，不进行向下嵌套折叠
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
            var yearGroups = wordList.GroupBy(w => w.CreatedAt.Year).OrderByDescending(g => g.Key);

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
                    // 季度分组 (Q1: 1~3月, Q2: 4~6月, Q3: 7~9月, Q4: 10~12月)
                    var quarterGroups = yg
                        .GroupBy(w => (w.CreatedAt.Month - 1) / 3 + 1)
                        .OrderByDescending(g => g.Key);

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
                    var weekGroups = yg
                        .GroupBy(w => calendar.GetWeekOfYear(w.CreatedAt, CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                        .OrderByDescending(g => g.Key);

                    foreach (var wg in weekGroups)
                    {
                        var weekNode = new DateHierarchyNode
                        {
                            Title = string.Format(loc.GetString("DateTreeNodeWeekSubFormat", "第 {0} 周 ({1} 词)"), wg.Key, wg.Count()),
                            Parent = yearNode
                        };
                        weekNode.WordIds.AddRange(wg.Select(w => w.Id));

                        var dayGroups = wg.GroupBy(w => w.CreatedAt.Date).OrderByDescending(g => g.Key);
                        foreach (var dg in dayGroups)
                        {
                            var dayNode = new DateHierarchyNode
                            {
                                Title = string.Format(loc.GetString("DateTreeNodeDayFormat", "{0:yyyy-MM-dd} ({1} 词)"), dg.Key, dg.Count()),
                                Parent = weekNode
                            };
                            dayNode.WordIds.AddRange(dg.Select(w => w.Id));
                            weekNode.Children.Add(dayNode);
                        }

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
                else // Default: Month
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

                        var weekGroups = mg
                            .GroupBy(w => calendar.GetWeekOfYear(w.CreatedAt, CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                            .OrderByDescending(g => g.Key);

                        foreach (var wg in weekGroups)
                        {
                            var weekNode = new DateHierarchyNode
                            {
                                Title = string.Format(loc.GetString("DateTreeNodeWeekSubFormat", "第 {0} 周 ({1} 词)"), wg.Key, wg.Count()),
                                Parent = monthNode
                            };
                            weekNode.WordIds.AddRange(wg.Select(w => w.Id));

                            var dayGroups = wg.GroupBy(w => w.CreatedAt.Date).OrderByDescending(g => g.Key);
                            foreach (var dg in dayGroups)
                            {
                                var dayNode = new DateHierarchyNode
                                {
                                    Title = string.Format(loc.GetString("DateTreeNodeDayFormat", "{0:yyyy-MM-dd} ({1} 词)"), dg.Key, dg.Count()),
                                    Parent = weekNode
                                };
                                dayNode.WordIds.AddRange(dg.Select(w => w.Id));
                                weekNode.Children.Add(dayNode);
                            }

                            monthNode.Children.Add(weekNode);
                        }

                        yearNode.Children.Add(monthNode);
                    }
                }

                DateTreeNodes.Add(yearNode);
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

        public async Task SelectNodeAsync(DateHierarchyNode node)
        {
            _selectedDateNode = node;
            SelectedWordList = null;
            UpdateCurrentViewTitle();

            var ids = node.GetAllContainedWordIds().ToList();
            var words = await _databaseService.GetWordsByIdsAsync(ids);

            ActiveWords.Clear();
            foreach (var w in words)
            {
                ActiveWords.Add(w);
            }
        }

        public async Task SelectWordListAsync(WordList list)
        {
            _selectedDateNode = null;
            SelectedWordList = list;
            UpdateCurrentViewTitle();

            var words = await _databaseService.GetWordsByListIdAsync(list.Id);
            ActiveWords.Clear();
            foreach (var w in words)
            {
                ActiveWords.Add(w);
            }
        }

        [RelayCommand]
        public async Task CreateListFromTreeSelectionAsync()
        {
            if (!await _createLock.WaitAsync(0))
            {
                return; // 已有创建操作正在执行，直接拦截重入
            }
            IsCreatingList = true;
            try
            {
                var selectedIds = new HashSet<int>();
                foreach (var rootNode in DateTreeNodes)
                {
                    foreach (var id in rootNode.GetAllSelectedWordIds())
                    {
                        selectedIds.Add(id);
                    }
                }

                var loc = LocalizationService.Instance;
                if (selectedIds.Count == 0)
                {
                    StatusMessage = loc.GetString("DialogMsgPleaseSelectTreeWordsToPack", "请先在时间层级树中勾选想要打包的日期或节点！");
                    return;
                }

                string defaultPrefix = loc.GetString("DefaultDateListNamePrefix", "时间词单_");
                string name = string.IsNullOrWhiteSpace(NewListName)
                    ? $"{defaultPrefix}{DateTime.Now:MMdd_HHmmss}"
                    : NewListName.Trim();

                CrashLogger.LogInfo($"CreateListFromTreeSelectionAsync: creating '{name}' with {selectedIds.Count} words");
                var list = await _databaseService.CreateWordListAsync(name, selectedIds);
                int count = list.WordCount > 0 ? list.WordCount : selectedIds.Count;
                StatusMessage = string.Format(loc.GetString("DialogMsgWordListCreatedSuccessFormat", "已成功创建词单「{0}」，共包含 {1} 个单词。"), list.Name, count);
                NewListName = string.Empty;

                // 创建完成后，立即清空所有树节点的勾选状态，防止残留勾选重复打包
                foreach (var rootNode in DateTreeNodes)
                {
                    rootNode.SetChecked(false, true, true);
                }

                await LoadDataAsync();
                var created = WordLists.FirstOrDefault(l => l.Id == list.Id);
                if (created != null)
                {
                    await SelectWordListAsync(created);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListViewModel.CreateListFromTreeSelectionAsync");
                var loc = LocalizationService.Instance;
                StatusMessage = string.Format(loc.GetString("DialogMsgWordListCreateFailedFormat", "创建词单失败：{0}"), ex.Message);
            }
            finally
            {
                IsCreatingList = false;
                _createLock.Release();
            }
        }

        public async Task CreateWordListFromWordsAsync(string name, IEnumerable<int> wordIds)
        {
            if (!await _createLock.WaitAsync(0))
            {
                return; // 已有创建操作正在执行，直接拦截重入
            }
            IsCreatingList = true;
            try
            {
                var ids = wordIds.ToList();
                if (ids.Count == 0) return;

                var loc = LocalizationService.Instance;
                string defaultPrefix = loc.GetString("DefaultCustomListNamePrefix", "自选词单_");
                string listName = string.IsNullOrWhiteSpace(name)
                    ? $"{defaultPrefix}{DateTime.Now:MMdd_HHmmss}"
                    : name.Trim();

                CrashLogger.LogInfo($"CreateWordListFromWordsAsync: creating '{listName}' with {ids.Count} words");
                var list = await _databaseService.CreateWordListAsync(listName, ids);
                StatusMessage = string.Empty;
                await LoadDataAsync();
                var created = WordLists.FirstOrDefault(l => l.Id == list.Id);
                if (created != null)
                {
                    await SelectWordListAsync(created);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListViewModel.CreateWordListFromWordsAsync");
                var loc = LocalizationService.Instance;
                StatusMessage = string.Format(loc.GetString("DialogMsgWordListCreateFailedFormat", "创建词单失败：{0}"), ex.Message);
            }
            finally
            {
                IsCreatingList = false;
                _createLock.Release();
            }
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

        public async Task DeleteWordListCompletelyAsync(WordList list)
        {
            if (SelectedWordList?.Id == list.Id)
            {
                SelectedWordList = null;
            }
            await _databaseService.DeleteWordListCompletelyAsync(list.Id);
            StatusMessage = string.Format(LocalizationService.Instance.GetString("MsgWordListDeletedCompletely", "已彻底删除词单「{0}」及其全部单词。"), list.Name);
            await LoadDataAsync();
        }

        public async Task BatchMoveWordsAsync(IEnumerable<Word> words, WordList? targetList)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            await _databaseService.MoveWordsToListAsync(list.Select(w => w.Id), targetList?.Id);
            string targetName = targetList == null ? "未归档" : $"词单「{targetList.Name}」";
            StatusMessage = $"已将 {list.Count} 个单词移动至 {targetName}。";
            await LoadDataAsync();
        }

        [RelayCommand]
        public async Task ExportCurrentViewToWebAsync()
        {
            try
            {
                var wordsToExport = ActiveWords.Count > 0 ? ActiveWords : AllWords;
                await _webExportService.OpenInBrowserAsync(wordsToExport);
                StatusMessage = "网页词库已成功导出并在系统默认浏览器中打开！";
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordListViewModel.ExportCurrentViewToWebAsync");
                StatusMessage = $"导出失败：{ex.Message}";
            }
        }
    }
}
