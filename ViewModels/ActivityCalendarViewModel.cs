using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NihongoVocab.Services;
using Windows.UI;

namespace NihongoVocab.ViewModels
{
    public class MonthCalendarDayItem
    {
        public DateTime Date { get; set; }
        public int DayNumber => Date.Day;
        public bool IsCurrentMonth { get; set; }
        public bool IsToday => Date.Date == DateTime.Today;
        public int StudyCount { get; set; }
        public int ImportCount { get; set; }
        public bool HasActivity => StudyCount > 0 || ImportCount > 0;

        public List<string> ImportedWords { get; set; } = new();
        public List<string> StudiedWords { get; set; } = new();

        public Visibility StudyBadgeVisibility => StudyCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string StudyBadgeText => string.Format(LocalizationService.Instance.GetString("ReviewBadgeFormat", "复习 {0}"), StudyCount);

        public Visibility ImportBadgeVisibility => ImportCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ImportBadgeText => string.Format(LocalizationService.Instance.GetString("ImportBadgeFormat", "收录 {0}"), ImportCount);

        public Visibility ActivityDotVisibility => (HasActivity && !IsToday) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NoActivityVisibility => (!HasActivity) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasImportedVisibility => ImportedWords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasStudiedVisibility => StudiedWords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string NoActivityTip => LocalizationService.Instance.GetString("NoActivityDayTip", "当日暂无收录或打卡记录");

        public string DetailHeaderTitle => $"{Date:yyyy-MM-dd} ({(IsToday ? LocalizationService.Instance.GetString("ButtonToday", "今日") : Date.ToString("ddd"))})";

        public string ImportedWordsSummary
        {
            get
            {
                if (ImportedWords == null || ImportedWords.Count == 0) return string.Empty;
                var sample = ImportedWords.Take(8).ToList();
                string str = string.Join("、", sample);
                if (ImportedWords.Count > 8) str += string.Format(LocalizationService.Instance.GetString("WordsSummaryEtcFormat", " 等共 {0} 词"), ImportedWords.Count);
                return str;
            }
        }

        public string StudiedWordsSummary
        {
            get
            {
                if (StudiedWords == null || StudiedWords.Count == 0) return string.Empty;
                var sample = StudiedWords.Take(8).ToList();
                string str = string.Join("、", sample);
                if (StudiedWords.Count > 8) str += string.Format(LocalizationService.Instance.GetString("WordsSummaryEtcFormat", " 等共 {0} 词"), StudiedWords.Count);
                return str;
            }
        }

        private static readonly Brush TodayBg = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
        private static readonly Brush BothActiveBg = new SolidColorBrush(Color.FromArgb(200, 160, 160, 160));
        private static readonly Brush StudyActiveBg = new SolidColorBrush(Color.FromArgb(160, 180, 180, 180));
        private static readonly Brush ImportActiveBg = new SolidColorBrush(Color.FromArgb(120, 205, 205, 205));
        private static readonly Brush TransparentBg = new SolidColorBrush(Colors.Transparent);

        private static readonly Brush WhiteFg = new SolidColorBrush(Colors.White);
        private static readonly Brush NotCurrentMonthFg = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128));
        private static readonly Brush ActiveFg = new SolidColorBrush(Color.FromArgb(255, 20, 20, 20));
        private static readonly Brush NormalDayFg = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30));

        public Brush DayBackground
        {
            get
            {
                if (IsToday) return TodayBg;
                if (StudyCount > 0 && ImportCount > 0) return BothActiveBg;
                if (StudyCount > 0) return StudyActiveBg;
                if (ImportCount > 0) return ImportActiveBg;
                return TransparentBg;
            }
        }

        public Brush DayForeground
        {
            get
            {
                if (IsToday) return WhiteFg;
                if (!IsCurrentMonth) return NotCurrentMonthFg;
                if (HasActivity) return ActiveFg;
                return NormalDayFg;
            }
        }

        public Windows.UI.Text.FontWeight DayFontWeight => (IsToday || HasActivity) 
            ? Microsoft.UI.Text.FontWeights.SemiBold 
            : Microsoft.UI.Text.FontWeights.Normal;
    }

    public class YearMonthItem
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthTitle
        {
            get
            {
                var lang = LocalizationService.Instance.CurrentLanguage;
                if (lang == "en-US")
                {
                    return Month switch
                    {
                        1 => "Jan", 2 => "Feb", 3 => "Mar", 4 => "Apr",
                        5 => "May", 6 => "Jun", 7 => "Jul", 8 => "Aug",
                        9 => "Sep", 10 => "Oct", 11 => "Nov", 12 => "Dec",
                        _ => $"{Month}"
                    };
                }
                if (lang == "ja-JP")
                {
                    return $"{Month}月";
                }
                return Month switch
                {
                    1 => "一月", 2 => "二月", 3 => "三月", 4 => "四月",
                    5 => "五月", 6 => "六月", 7 => "七月", 8 => "八月",
                    9 => "九月", 10 => "十月", 11 => "十一月", 12 => "十二月",
                    _ => $"{Month}月"
                };
            }
        }
        public string SunShort => LocalizationService.Instance.GetString("SunShort", "日");
        public string MonShort => LocalizationService.Instance.GetString("MonShort", "一");
        public string TueShort => LocalizationService.Instance.GetString("TueShort", "二");
        public string WedShort => LocalizationService.Instance.GetString("WedShort", "三");
        public string ThuShort => LocalizationService.Instance.GetString("ThuShort", "四");
        public string FriShort => LocalizationService.Instance.GetString("FriShort", "五");
        public string SatShort => LocalizationService.Instance.GetString("SatShort", "六");

        public List<MonthCalendarDayItem> Days { get; set; } = new();
    }

    public partial class ActivityCalendarViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;

        [ObservableProperty]
        private Dictionary<DateTime, int> _importHeatmapData = new();

        [ObservableProperty]
        private Dictionary<DateTime, int> _studyHeatmapData = new();

        private Dictionary<DateTime, List<string>> _importedWordsDict = new();
        private Dictionary<DateTime, List<string>> _studiedWordsDict = new();

        [ObservableProperty]
        private int _totalImportDays = 0;

        [ObservableProperty]
        private int _totalStudyDays = 0;

        [ObservableProperty]
        private int _totalWords = 0;

        [ObservableProperty]
        private DayActivityDetail? _selectedDayDetail;

        [ObservableProperty]
        private int _currentYear = DateTime.Today.Year;

        [ObservableProperty]
        private int _currentMonth = DateTime.Today.Month;

        [ObservableProperty]
        private string _currentViewMode = "Year"; // "Year" 或 "Month"

        public string HeaderTitle => CurrentViewMode == "Year" 
            ? $"{CurrentYear}{LocalizationService.Instance.GetString("YearUnit", "年")}" 
            : $"{CurrentYear}{LocalizationService.Instance.GetString("YearUnit", "年")} {CurrentMonth}{LocalizationService.Instance.GetString("MonthUnit", "月")}";

        public string ViewModeButtonText => CurrentViewMode == "Year" 
            ? LocalizationService.Instance.GetString("YearUnit", "年") 
            : LocalizationService.Instance.GetString("MonthUnit", "月");

        public string YearViewOption => LocalizationService.Instance.GetString("YearViewOption", "年 (12个月全景平铺)");
        public string MonthViewOption => LocalizationService.Instance.GetString("MonthViewOption", "月 (单月份放大明细)");
        public string TooltipPrevPeriod => LocalizationService.Instance.GetString("TooltipPrevPeriod", "上一周期");
        public string ButtonToday => LocalizationService.Instance.GetString("ButtonToday", "今天");
        public string TooltipNextPeriod => LocalizationService.Instance.GetString("TooltipNextPeriod", "下一周期");
        public string TotalWordsRecordedLabel => LocalizationService.Instance.GetString("TotalWordsRecordedLabel", "累计收录词数:");
        public string TotalImportDaysLabel => LocalizationService.Instance.GetString("TotalImportDaysLabel", "累计导入天数:");
        public string TotalReviewDaysLabel => LocalizationService.Instance.GetString("TotalReviewDaysLabel", "累计复习打卡天数:");
        public string BackToYearView => LocalizationService.Instance.GetString("BackToYearView", "返回全年平铺视图");
        public string SunShort => LocalizationService.Instance.GetString("SunShort", "日");
        public string MonShort => LocalizationService.Instance.GetString("MonShort", "一");
        public string TueShort => LocalizationService.Instance.GetString("TueShort", "二");
        public string WedShort => LocalizationService.Instance.GetString("WedShort", "三");
        public string ThuShort => LocalizationService.Instance.GetString("ThuShort", "四");
        public string FriShort => LocalizationService.Instance.GetString("FriShort", "五");
        public string SatShort => LocalizationService.Instance.GetString("SatShort", "六");
        public string SunLong => LocalizationService.Instance.GetString("SunLong", "周日");
        public string MonLong => LocalizationService.Instance.GetString("MonLong", "周一");
        public string TueLong => LocalizationService.Instance.GetString("TueLong", "周二");
        public string WedLong => LocalizationService.Instance.GetString("WedLong", "周三");
        public string ThuLong => LocalizationService.Instance.GetString("ThuLong", "周四");
        public string FriLong => LocalizationService.Instance.GetString("FriLong", "周五");
        public string SatLong => LocalizationService.Instance.GetString("SatLong", "周六");

        public ObservableCollection<YearMonthItem> YearMonths { get; } = new();
        public ObservableCollection<MonthCalendarDayItem> CurrentMonthDays { get; } = new();

        public ActivityCalendarViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                OnPropertyChanged(string.Empty);
                _lastBuiltYear = -1; // 强制刷新月份网格标题（如 1月、1月/Jan 等）
                BuildGrids();
            };
        }

        public async Task LoadDataAsync()
        {
            try
            {
                var stats = await _databaseService.GetDashboardStatsAsync();
                TotalImportDays = stats.TotalImportDays;
                TotalStudyDays = stats.TotalStudyDays;
                TotalWords = stats.TotalWords;

                ImportHeatmapData = await _databaseService.GetImportHeatmapDataAsync();
                StudyHeatmapData = await _databaseService.GetStudyHeatmapDataAsync();

                var (imported, studied) = await _databaseService.GetCalendarActivityDetailsDictAsync();
                _importedWordsDict = imported;
                _studiedWordsDict = studied;

                BuildGrids();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ActivityCalendarViewModel.LoadDataAsync");
            }
        }

        private int _lastBuiltYear = -1;

        public void SetViewMode(string mode)
        {
            CurrentViewMode = mode;
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(ViewModeButtonText));

            if (mode == "Month")
            {
                BuildMonthGrid();
            }
            else
            {
                BuildYearGridIfNeeded();
            }
        }

        public void NavigatePrevious()
        {
            if (CurrentViewMode == "Year")
            {
                CurrentYear--;
                BuildYearGrid(true);
            }
            else
            {
                CurrentMonth--;
                if (CurrentMonth < 1)
                {
                    CurrentMonth = 12;
                    CurrentYear--;
                }
                BuildMonthGrid();
            }
            OnPropertyChanged(nameof(HeaderTitle));
        }

        public void NavigateNext()
        {
            if (CurrentViewMode == "Year")
            {
                CurrentYear++;
                BuildYearGrid(true);
            }
            else
            {
                CurrentMonth++;
                if (CurrentMonth > 12)
                {
                    CurrentMonth = 1;
                    CurrentYear++;
                }
                BuildMonthGrid();
            }
            OnPropertyChanged(nameof(HeaderTitle));
        }

        public void NavigateToday()
        {
            CurrentYear = DateTime.Today.Year;
            CurrentMonth = DateTime.Today.Month;
            OnPropertyChanged(nameof(HeaderTitle));
            BuildGrids();
        }

        public void DrillDownToMonth(int month)
        {
            CurrentMonth = month;
            SetViewMode("Month");
        }

        public void BuildGrids()
        {
            RunOnUIThread(() =>
            {
                BuildYearGridInternal();
                BuildMonthGridInternal();
            });
        }

        private void BuildYearGridIfNeeded()
        {
            if (_lastBuiltYear != CurrentYear || YearMonths.Count == 0)
            {
                RunOnUIThread(BuildYearGridInternal);
            }
        }

        private void BuildYearGrid(bool force = false)
        {
            if (force || _lastBuiltYear != CurrentYear || YearMonths.Count == 0)
            {
                RunOnUIThread(BuildYearGridInternal);
            }
        }

        private void BuildMonthGrid()
        {
            RunOnUIThread(BuildMonthGridInternal);
        }

        private void BuildYearGridInternal()
        {
            YearMonths.Clear();
            for (int m = 1; m <= 12; m++)
            {
                var monthItem = new YearMonthItem
                {
                    Year = CurrentYear,
                    Month = m,
                    Days = Generate42DaysForMonth(CurrentYear, m)
                };
                YearMonths.Add(monthItem);
            }
            _lastBuiltYear = CurrentYear;
        }

        private void BuildMonthGridInternal()
        {
            CurrentMonthDays.Clear();
            var monthDays = Generate42DaysForMonth(CurrentYear, CurrentMonth);
            foreach (var d in monthDays)
            {
                CurrentMonthDays.Add(d);
            }
        }

        private void RunOnUIThread(Action action)
        {
            var dispatcher = App.UIThreadDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        private List<MonthCalendarDayItem> Generate42DaysForMonth(int year, int month)
        {
            var result = new List<MonthCalendarDayItem>(42);
            var firstDayOfMonth = new DateTime(year, month, 1);

            // 按照附图标准：周日（Sunday=0）为一周的第一列
            int offset = (int)firstDayOfMonth.DayOfWeek;
            var startDate = firstDayOfMonth.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                var date = startDate.AddDays(i);
                int study = StudyHeatmapData.TryGetValue(date.Date, out var s) ? s : 0;
                int import = ImportHeatmapData.TryGetValue(date.Date, out var im) ? im : 0;

                _importedWordsDict.TryGetValue(date.Date, out var impWords);
                _studiedWordsDict.TryGetValue(date.Date, out var stdWords);

                result.Add(new MonthCalendarDayItem
                {
                    Date = date,
                    IsCurrentMonth = date.Month == month,
                    StudyCount = study,
                    ImportCount = import,
                    ImportedWords = impWords ?? new(),
                    StudiedWords = stdWords ?? new()
                });
            }
            return result;
        }

        public async Task<DayActivityDetail> GetDayDetailAsync(DateTime date)
        {
            try
            {
                var detail = await _databaseService.GetDayDetailAsync(date);
                SelectedDayDetail = detail;
                return detail;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ActivityCalendarViewModel.GetDayDetailAsync");
                return new DayActivityDetail { Date = date };
            }
        }
    }
}
