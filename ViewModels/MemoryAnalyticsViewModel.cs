using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public class WordListFilterOption
    {
        public int? Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public partial class MemoryAnalyticsViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private DashboardStats _stats = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private WordListFilterOption? _selectedOption;

        public ObservableCollection<WordListFilterOption> FilterOptions { get; } = new();
        public ObservableCollection<Word> SearchResults { get; } = new();

        public string MemoryAnalyticsTitle => LocalizationService.Instance.GetString("MemoryAnalyticsTitle", "记忆洞察");
        public string AnalysisScopeLabel => LocalizationService.Instance.GetString("AnalysisScopeLabel", "分析范围:");
        public string FsrsAvgRetentionLabel => LocalizationService.Instance.GetString("FsrsAvgRetentionLabel", "FSRS 平均记忆留存率");
        public string FsrsRetentionDesc => LocalizationService.Instance.GetString("FsrsRetentionDesc", "当前活跃复习池整体记忆概率");
        public string FsrsAvgStabilityLabel => LocalizationService.Instance.GetString("FsrsAvgStabilityLabel", "平均记忆稳定性 (半衰期)");
        public string FsrsStabilityDesc => LocalizationService.Instance.GetString("FsrsStabilityDesc", "遗忘至 90% 所需的平均时间间隔");
        public string IndependentMasteredPoolLabel => LocalizationService.Instance.GetString("IndependentMasteredPoolLabel", "独立已掌握归档池");
        public string IndependentMasteredPoolDesc => LocalizationService.Instance.GetString("IndependentMasteredPoolDesc", "已完全脱离日常遗忘调度流");
        public string FsrsDistributionTitle => LocalizationService.Instance.GetString("FsrsDistributionTitle", "FSRS 动态记忆状态层级分布");
        public string TooltipBrowseStateWords => LocalizationService.Instance.GetString("TooltipBrowseStateWords", "点击浏览该状态的单词列表");
        public string StateNewHeader => LocalizationService.Instance.GetString("StateNewHeader", "未学习 (New)");
        public string StateNewDesc => LocalizationService.Instance.GetString("StateNewDesc", "尚未开始打卡复习的新导入词");
        public string StateLearningHeader => LocalizationService.Instance.GetString("StateLearningHeader", "初学中 (Learning)");
        public string StateLearningDesc => LocalizationService.Instance.GetString("StateLearningDesc", "打卡次数较少，正在建立初始记忆痕迹");
        public string StateReviewHeader => LocalizationService.Instance.GetString("StateReviewHeader", "复习中 (Review)");
        public string StateReviewDesc => LocalizationService.Instance.GetString("StateReviewDesc", "记忆稳定性上升，处于中长期间隔复习");
        public string StateRelearningHeader => LocalizationService.Instance.GetString("StateRelearningHeader", "重新学习 (Relearning)");
        public string StateRelearningDesc => LocalizationService.Instance.GetString("StateRelearningDesc", "发生遗忘，稳定性重置进入巩固阶段");
        public string IndividualCurveTitle => LocalizationService.Instance.GetString("IndividualCurveTitle", "个体单词遗忘曲线与记忆健康度");
        public string IndividualCurveDesc => LocalizationService.Instance.GetString("IndividualCurveDesc", "输入任一单词，实时推演记忆留存衰减轨迹与复习时机");
        public string SearchWordCurvePlaceholder => LocalizationService.Instance.GetString("SearchWordCurvePlaceholder", "搜索单词分析记忆曲线...");
        public string TooltipWordCurveItem => LocalizationService.Instance.GetString("TooltipWordCurveItem", "左键点击查看个体学习记录与记忆曲线，中键点击复制");
        public string TooltipRetention => LocalizationService.Instance.GetString("TooltipRetention", "基于 FSRS 记忆衰减公式计算得出的当前瞬间记忆召回概率");
        public string TooltipStability => LocalizationService.Instance.GetString("TooltipStability", "FSRS 记忆稳定性指标：在没有任何复习介入的情况下，该词留存率跌至 90% 所需的理论天数");
        public string TooltipDifficulty => LocalizationService.Instance.GetString("TooltipDifficulty", "FSRS 单词内在记忆难度（1-10分）。分值越高表示遗忘频次越高或记忆耗费脑力越多");
        public string TooltipReps => LocalizationService.Instance.GetString("TooltipReps", "累计有效复习轮数");

        public string ActiveCountFormatText => string.Format(LocalizationService.Instance.GetString("FsrsActiveTotalFormat", "FSRS 活跃总数: {0} 词"), Stats.FsrsActiveCount);
        public string StabilityDaysFormatText => string.Format("{0:F1} {1}", Stats.AverageStability, LocalizationService.Instance.GetString("DaysUnit", "天"));

        public MemoryAnalyticsViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            DatabaseService.DataChanged += OnDatabaseDataChanged;
            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                OnPropertyChanged(string.Empty);
                _ = LoadDataAsync();
            };
        }

        private void OnDatabaseDataChanged()
        {
            App.RunOnUIThread(async () =>
            {
                await RefreshStatsAsync();
            });
        }

        public async Task LoadDataAsync()
        {
            try
            {
                var previousId = SelectedOption?.Id;
                FilterOptions.Clear();
                FilterOptions.Add(new WordListFilterOption { Id = null, DisplayName = LocalizationService.Instance.GetString("AllWordsOverviewOption", "全库总览（全部单词）") });

                var lists = await _databaseService.GetAllWordListsAsync();
                foreach (var l in lists)
                {
                    FilterOptions.Add(new WordListFilterOption { Id = l.Id, DisplayName = string.Format(LocalizationService.Instance.GetString("WordListOptionFormat", "词单：{0} ({1}词)"), l.Name, l.WordCount) });
                }

                if (previousId.HasValue)
                {
                    SelectedOption = FilterOptions.FirstOrDefault(o => o.Id == previousId.Value) ?? FilterOptions[0];
                }
                else
                {
                    int savedId = UserPreferenceService.Instance.GetInt("MemoryAnalytics_SelectedListId", -1);
                    if (savedId >= 0)
                    {
                        SelectedOption = FilterOptions.FirstOrDefault(o => o.Id == savedId) ?? FilterOptions[0];
                    }
                    else
                    {
                        SelectedOption = FilterOptions[0];
                    }
                }

                await RefreshStatsAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsViewModel.LoadDataAsync");
            }
        }

        partial void OnSelectedOptionChanged(WordListFilterOption? value)
        {
            UserPreferenceService.Instance.SetInt("MemoryAnalytics_SelectedListId", value?.Id ?? -1);
            _ = RefreshStatsAsync();
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = SearchAsync();
        }

        public async Task RefreshStatsAsync()
        {
            try
            {
                int? listId = SelectedOption?.Id;
                Stats = await _databaseService.GetDashboardStatsAsync(listId);
                OnPropertyChanged(nameof(ActiveCountFormatText));
                OnPropertyChanged(nameof(StabilityDaysFormatText));
                await SearchAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsViewModel.RefreshStatsAsync");
            }
        }

        [RelayCommand]
        public async Task SearchAsync()
        {
            try
            {
                int? listId = SelectedOption?.Id;
                var words = await _databaseService.SearchWordsAsync(SearchQuery, listId);
                SearchResults.Clear();
                foreach (var w in words)
                {
                    SearchResults.Add(w);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsViewModel.SearchAsync");
            }
        }

        public async Task<System.Collections.Generic.List<ReviewLog>> GetWordReviewLogsAsync(int wordId)
        {
            return await _databaseService.GetWordReviewLogsAsync(wordId);
        }

        public async Task<System.Collections.Generic.List<Word>> GetWordsByStateAsync(int state)
        {
            int? listId = SelectedOption?.Id;
            return await _databaseService.GetWordsByStateAsync(state, listId);
        }
    }
}
