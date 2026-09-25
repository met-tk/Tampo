using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Windows.UI;

namespace NihongoVocab.ViewModels
{
    public class TodayWordCardItem : ObservableObject
    {
        public TodayReviewItem RawItem { get; }

        public int LogId => RawItem.LogId;
        public int WordId => RawItem.WordId;
        public string WordText => RawItem.WordText;
        public string ReviewTimeText => RawItem.ReviewTimeText;

        private int _rating;
        public int Rating
        {
            get => _rating;
            set
            {
                if (SetProperty(ref _rating, value))
                {
                    OnPropertyChanged(nameof(RatingText));
                    OnPropertyChanged(nameof(CardBackground));
                    OnPropertyChanged(nameof(CardBorderBrush));
                    OnPropertyChanged(nameof(CardForeground));
                    OnPropertyChanged(nameof(StatusPillBackground));
                    OnPropertyChanged(nameof(StatusPillForeground));
                }
            }
        }

        public string RatingText => Rating == 3
            ? LocalizationService.Instance.GetString("RememberText", "记得")
            : LocalizationService.Instance.GetString("ForgetText", "遗忘");

        // 网格卡片背景色：绿色代表记得，红色代表遗忘（用户明确要求）
        public Brush CardBackground => Rating == 3
            ? new SolidColorBrush(Color.FromArgb(210, 22, 101, 52))     // 绿意底色 (#166534)
            : new SolidColorBrush(Color.FromArgb(210, 153, 27, 27));    // 红润底色 (#991B1B)

        public Brush CardBorderBrush => Rating == 3
            ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))     // 高亮翠绿边框 (#22C55E)
            : new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));    // 高亮绯红边框 (#EF4444)

        public Brush CardForeground => new SolidColorBrush(Colors.White);

        public Brush StatusPillBackground => Rating == 3
            ? new SolidColorBrush(Color.FromArgb(40, 34, 197, 94))
            : new SolidColorBrush(Color.FromArgb(40, 239, 68, 68));

        public Brush StatusPillForeground => Rating == 3
            ? new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
            : new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));

        public string ClickToggleRatingTip => LocalizationService.Instance.GetString("ClickToggleRatingTip", "左键单击直接在「记得」与「遗忘」之间切换");
        public string ClickToggleOppositeTip => LocalizationService.Instance.GetString("ClickToggleOppositeTip", "点击切换为相反状态");
        public string RevertTodayRecordTip => LocalizationService.Instance.GetString("RevertTodayRecordTip", "撤销此单词的今日记录");

        public TodayWordCardItem(TodayReviewItem rawItem)
        {
            RawItem = rawItem;
            _rating = rawItem.Rating;
            LocalizationService.Instance.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
        }
    }

    public partial class TodayStudyViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        private List<TodayWordCardItem> _allItems = new();

        public ObservableCollection<TodayWordCardItem> DisplayItems { get; } = new();

        [ObservableProperty]
        private string _statusFilter = "All"; // "All", "Remember", "Forget"

        [ObservableProperty]
        private string _viewMode = "Grid"; // "List" 或 "Grid"

        [ObservableProperty]
        private int _totalTodayCount = 0;

        [ObservableProperty]
        private int _rememberCount = 0;

        [ObservableProperty]
        private int _forgetCount = 0;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public string TodayStudyTitle => LocalizationService.Instance.GetString("TodayStudyTitle", "今日学习");
        public string StatusTierLabel => LocalizationService.Instance.GetString("StatusTierLabel", "状态层级：");
        public string StatusTierAll => LocalizationService.Instance.GetString("StatusTierAll", "全部打卡");
        public string StatusTierRemember => LocalizationService.Instance.GetString("StatusTierRemember", "记得 (Good)");
        public string StatusTierForget => LocalizationService.Instance.GetString("StatusTierForget", "遗忘 (Again)");
        public string TodayLearnedLabel => LocalizationService.Instance.GetString("TodayLearnedLabel", "今日已学:");
        public string RememberLabel => LocalizationService.Instance.GetString("RememberLabel", "记得:");
        public string ForgetLabel => LocalizationService.Instance.GetString("ForgetLabel", "遗忘:");
        public string GridViewText => LocalizationService.Instance.GetString("GridViewText", "网格显示");
        public string GridViewToolTip => LocalizationService.Instance.GetString("GridViewToolTip", "网格视图（红绿卡片，点击直接切换状态）");
        public string ListViewText => LocalizationService.Instance.GetString("ListViewText", "列表显示");
        public string ListViewToolTip => LocalizationService.Instance.GetString("ListViewToolTip", "列表视图（条目清晰，右侧提供状态切换按钮）");
        public string ButtonRefresh => LocalizationService.Instance.GetString("ButtonRefresh", "刷新");
        public string ClickToggleRatingTip => LocalizationService.Instance.GetString("ClickToggleRatingTip", "左键单击直接在「记得」与「遗忘」之间切换");
        public string ClickToggleOppositeTip => LocalizationService.Instance.GetString("ClickToggleOppositeTip", "点击切换为相反状态");
        public string RevertTodayRecordTip => LocalizationService.Instance.GetString("RevertTodayRecordTip", "撤销此单词的今日记录");

        public TodayStudyViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            LocalizationService.Instance.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
            DatabaseService.DataChanged += OnDatabaseDataChanged;
        }

        private void OnDatabaseDataChanged()
        {
            var dispatcher = App.UIThreadDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(async () => await LoadDataAsync());
            }
            else
            {
                _ = LoadDataAsync();
            }
        }

        public async Task LoadDataAsync()
        {
            try
            {
                var rawItems = await _databaseService.GetTodayReviewItemsAsync();
                _allItems = rawItems.Select(r => new TodayWordCardItem(r)).ToList();

                TotalTodayCount = _allItems.Count;
                RememberCount = _allItems.Count(i => i.Rating == 3);
                ForgetCount = _allItems.Count(i => i.Rating == 1);

                ApplyFilter();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyViewModel.LoadDataAsync");
            }
        }

        partial void OnStatusFilterChanged(string value) => ApplyFilter();

        public void ApplyFilter()
        {
            DisplayItems.Clear();
            var query = _allItems.AsEnumerable();

            if (StatusFilter == "Remember")
            {
                query = query.Where(i => i.Rating == 3);
            }
            else if (StatusFilter == "Forget")
            {
                query = query.Where(i => i.Rating == 1);
            }

            foreach (var item in query)
            {
                DisplayItems.Add(item);
            }
        }

        public async Task ToggleItemRatingAsync(TodayWordCardItem item)
        {
            try
            {
                int newRating = item.Rating == 3 ? 1 : 3;
                await _databaseService.ChangeTodayReviewRatingAsync(item.LogId, newRating);
                item.Rating = newRating;

                RememberCount = _allItems.Count(i => i.Rating == 3);
                ForgetCount = _allItems.Count(i => i.Rating == 1);

                StatusMessage = $"已将「{item.WordText}」今日标记切换为【{(newRating == 3 ? "记得" : "遗忘")}】";

                // 如果当前选了单状态过滤（如只看记得），状态反转后平滑重新筛选
                if (StatusFilter != "All")
                {
                    ApplyFilter();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyViewModel.ToggleItemRatingAsync");
            }
        }

        public async Task RevertItemAsync(TodayWordCardItem item)
        {
            try
            {
                await _databaseService.RevertTodayReviewAsync(item.LogId);
                _allItems.Remove(item);
                DisplayItems.Remove(item);

                TotalTodayCount = _allItems.Count;
                RememberCount = _allItems.Count(i => i.Rating == 3);
                ForgetCount = _allItems.Count(i => i.Rating == 1);

                StatusMessage = $"已撤销「{item.WordText}」的今日学习记录";
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyViewModel.RevertItemAsync");
            }
        }
    }
}
