using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class RecentStudyViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        private List<Word> _rawWords = new();

        public ObservableCollection<Word> DisplayWords { get; } = new();

        [ObservableProperty]
        private string _timeSpanLevel = "Week"; // "Day", "Week", "Month", "Quarter"

        [ObservableProperty]
        private string _selectedStateFilter = "All"; // "All", "New", "Learning", "Review", "Mastered"

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private int _totalFoundCount = 0;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public string RecentStudyTitle => LocalizationService.Instance.GetString("RecentStudyTitle", "最近学习");
        public string TimeSpanLabel => LocalizationService.Instance.GetString("TimeSpanLabel", "时间层级：");
        public string TimeSpanDay => LocalizationService.Instance.GetString("TimeSpanDay", "日 (最近24h)");
        public string TimeSpanWeek => LocalizationService.Instance.GetString("TimeSpanWeek", "周 (最近7天)");
        public string TimeSpanMonth => LocalizationService.Instance.GetString("TimeSpanMonth", "月 (最近30天)");
        public string TimeSpanQuarter => LocalizationService.Instance.GetString("TimeSpanQuarter", "季度 (最近90天)");
        public string StateFilterLabel => LocalizationService.Instance.GetString("StateFilterLabel", "状态层级：");
        public string StateAll => LocalizationService.Instance.GetString("StateAll", "全部状态");
        public string StateNew => LocalizationService.Instance.GetString("StateNew", "未学习 (New)");
        public string StateLearning => LocalizationService.Instance.GetString("StateLearning", "学习中 (Learning)");
        public string StateReview => LocalizationService.Instance.GetString("StateReview", "复习中 (Review)");
        public string StateMastered => LocalizationService.Instance.GetString("StateMastered", "已掌握 (Mastered)");
        public string TotalFoundLabel => LocalizationService.Instance.GetString("TotalFoundLabel", "共命中:");
        public string SearchRecentWordsPlaceholder => LocalizationService.Instance.GetString("SearchRecentWordsPlaceholder", "搜索最近学习词汇...");
        public string ButtonRefresh => LocalizationService.Instance.GetString("ButtonRefresh", "刷新");
        public string SelectAllResults => LocalizationService.Instance.GetString("SelectAllResults", "全选当前结果");
        public string RightClickMenuTip => LocalizationService.Instance.GetString("RightClickMenuTip", "多选单词后右键已选词弹出操作菜单");
        public string MenuMarkNew => LocalizationService.Instance.GetString("MenuMarkNew", "标记为：未学习");
        public string MenuMarkMastered => LocalizationService.Instance.GetString("MenuMarkMastered", "标记为：已掌握");
        public string MenuReimportToday => LocalizationService.Instance.GetString("MenuReimportToday", "重新在今天导入");
        public string MenuDeleteWords => LocalizationService.Instance.GetString("MenuDeleteWords", "删除所选单词");

        public RecentStudyViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            LocalizationService.Instance.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
        }

        public async Task LoadDataAsync()
        {
            try
            {
                WordLearningState? stateParam = null;
                if (SelectedStateFilter != "All" && Enum.TryParse<WordLearningState>(SelectedStateFilter, out var parsedState))
                {
                    stateParam = parsedState;
                }

                _rawWords = await _databaseService.GetRecentStudyWordsAsync(TimeSpanLevel, stateParam);
                TotalFoundCount = _rawWords.Count;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "RecentStudyViewModel.LoadDataAsync");
            }
        }

        partial void OnTimeSpanLevelChanged(string value) => _ = LoadDataAsync();
        partial void OnSelectedStateFilterChanged(string value) => _ = LoadDataAsync();
        partial void OnSearchTextChanged(string value) => ApplyFilter();

        public void ApplyFilter()
        {
            DisplayWords.Clear();
            var query = _rawWords.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(w => w.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var w in query)
            {
                DisplayWords.Add(w);
            }
        }

        public async Task BatchUpdateStateAsync(IEnumerable<Word> words, WordLearningState newState)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            await _databaseService.UpdateWordsStateAsync(list.Select(w => w.Id), newState);
            StatusMessage = $"已将 {list.Count} 个单词状态变更为 {newState}";
            await LoadDataAsync();
        }

        public async Task DeleteWordsAsync(IEnumerable<Word> words)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            await _databaseService.DeleteWordsAsync(list.Select(w => w.Id));
            StatusMessage = $"已成功删除 {list.Count} 个单词";
            await LoadDataAsync();
        }

        public async Task ReimportWordsTodayAsync(IEnumerable<Word> words)
        {
            var list = words.ToList();
            if (list.Count == 0) return;

            var loc = LocalizationService.Instance;
            await _databaseService.ReimportWordsTodayAsync(list.Select(w => w.Id));
            StatusMessage = string.Format(loc.GetString("MsgReimportWordsTodaySuccessFormat", "已将 {0} 个单词重置并在今天重新导入"), list.Count);
            await LoadDataAsync();
        }
    }
}
