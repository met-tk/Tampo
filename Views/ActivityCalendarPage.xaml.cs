using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class ActivityCalendarPage : Page
    {
        public ActivityCalendarViewModel ViewModel { get; }

        public ActivityCalendarPage()
        {
            ViewModel = App.GetService<ActivityCalendarViewModel>();
            this.InitializeComponent();
            this.DataContext = ViewModel;
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            RestorePreferences();

            RootLayoutGrid.PointerPressed += Page_PointerPressed;
            MenuSelectYearViewItem.Click += MenuSelectYearView_Click;
            MenuSelectMonthViewItem.Click += MenuSelectMonthView_Click;
            PrevPeriodButton.Click += Prev_Click;
            TodayButton.Click += Today_Click;
            NextPeriodButton.Click += Next_Click;
            YearRepeater.PointerPressed += YearRepeater_PointerPressed;
            BackToYearViewButton.Click += BackToYearView_Click;

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            this.Loaded += (s, e) => UpdateLocalizedStrings();
            UpdateLocalizedStrings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLocalizedStrings();
                UpdateViewModeUI();
            });
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;

            if (TodayButton != null) TodayButton.Content = loc.GetString("ActivityCalendar_ButtonToday", "今天");

            if (PrevPeriodButton != null) ToolTipService.SetToolTip(PrevPeriodButton, loc.GetString("ActivityCalendar_TooltipPrevPeriod", "切换至上一周期"));
            if (NextPeriodButton != null) ToolTipService.SetToolTip(NextPeriodButton, loc.GetString("ActivityCalendar_TooltipNextPeriod", "切换至下一周期"));

            if (ViewModeDropDownButton != null) ViewModeDropDownButton.Content = loc.GetString("ActivityCalendar_ViewModeButton", "年份缩放");
            if (MenuSelectYearViewItem != null) MenuSelectYearViewItem.Text = loc.GetString("ActivityCalendar_MenuYearView", "年度概览 (12个月平铺)");
            if (MenuSelectMonthViewItem != null) MenuSelectMonthViewItem.Text = loc.GetString("ActivityCalendar_MenuMonthView", "单月特写 (高精日历格)");

            if (TotalWordsLabelTextBlock != null) TotalWordsLabelTextBlock.Text = loc.GetString("ActivityCalendar_TotalWordsLabel", "记录总词量");
            if (TotalImportDaysLabelTextBlock != null) TotalImportDaysLabelTextBlock.Text = loc.GetString("ActivityCalendar_TotalImportDaysLabel", "导入活跃天数");
            if (TotalStudyDaysLabelTextBlock != null) TotalStudyDaysLabelTextBlock.Text = loc.GetString("ActivityCalendar_TotalStudyDaysLabel", "复习打卡天数");

            if (BackToYearViewTextBlock != null) BackToYearViewTextBlock.Text = loc.GetString("ActivityCalendar_BackToYearView", "返回年视图");

            if (MonthWeekCol0 != null) MonthWeekCol0.Text = loc.GetString("WeekSun", "周日");
            if (MonthWeekCol1 != null) MonthWeekCol1.Text = loc.GetString("WeekMon", "周一");
            if (MonthWeekCol2 != null) MonthWeekCol2.Text = loc.GetString("WeekTue", "周二");
            if (MonthWeekCol3 != null) MonthWeekCol3.Text = loc.GetString("WeekWed", "周三");
            if (MonthWeekCol4 != null) MonthWeekCol4.Text = loc.GetString("WeekThu", "周四");
            if (MonthWeekCol5 != null) MonthWeekCol5.Text = loc.GetString("WeekFri", "周五");
            if (MonthWeekCol6 != null) MonthWeekCol6.Text = loc.GetString("WeekSat", "周六");
        }

        private void RestorePreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;
                string savedMode = pref.Get("Calendar_ViewMode", "Year");
                if (ViewModel != null && (savedMode == "Year" || savedMode == "Month"))
                {
                    ViewModel.SetViewMode(savedMode);
                }
                UpdateViewModeUI();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ActivityCalendarPage.RestorePreferences");
            }
        }

        private void OnDatabaseDataChanged()
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ViewModel.LoadDataAsync();
                UpdateViewModeUI();
            });
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
            DatabaseService.DataChanged += OnDatabaseDataChanged;

            try
            {
                await ViewModel.LoadDataAsync();
                UpdateViewModeUI();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ActivityCalendarPage.OnNavigatedTo");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigatePrevious();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateNext();
        }

        private void Today_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NavigateToday();
        }

        private void MenuSelectYearView_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode("Year");
            UserPreferenceService.Instance.Set("Calendar_ViewMode", "Year");
            UpdateViewModeUI();
        }

        private void MenuSelectMonthView_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode("Month");
            UserPreferenceService.Instance.Set("Calendar_ViewMode", "Month");
            UpdateViewModeUI();
        }

        private void MonthHeader_Click(object sender, RoutedEventArgs e)
        {
            var ym = (sender as FrameworkElement)?.Tag as YearMonthItem 
                     ?? (sender as FrameworkElement)?.DataContext as YearMonthItem;
            if (ym != null)
            {
                ViewModel.DrillDownToMonth(ym.Month);
                UserPreferenceService.Instance.Set("Calendar_ViewMode", "Month");
                UpdateViewModeUI();
            }
        }

        private void YearRepeater_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ym = (e.OriginalSource as FrameworkElement)?.DataContext as YearMonthItem 
                     ?? (e.OriginalSource as FrameworkElement)?.Tag as YearMonthItem;
            if (ym != null)
            {
                ViewModel.DrillDownToMonth(ym.Month);
                UserPreferenceService.Instance.Set("Calendar_ViewMode", "Month");
                UpdateViewModeUI();
            }
        }

        private void BackToYearView_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SetViewMode("Year");
            UserPreferenceService.Instance.Set("Calendar_ViewMode", "Year");
            UpdateViewModeUI();
        }

        private void UpdateViewModeUI()
        {
            if (ViewModel.CurrentViewMode == "Year")
            {
                YearScrollViewer.Visibility = Visibility.Visible;
                MonthDetailViewBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                YearScrollViewer.Visibility = Visibility.Collapsed;
                MonthDetailViewBorder.Visibility = Visibility.Visible;
            }
        }

        private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsXButton1Pressed) // 鼠标退回键 (Back / XButton1)
            {
                e.Handled = true;
                if (ViewModel.CurrentViewMode == "Month")
                {
                    ViewModel.SetViewMode("Year");
                    UpdateViewModeUI();
                }
            }
            else if (properties.IsXButton2Pressed) // 鼠标前进键 (Forward / XButton2)
            {
                e.Handled = true;
                if (ViewModel.CurrentViewMode == "Year")
                {
                    ViewModel.SetViewMode("Month");
                    UpdateViewModeUI();
                }
            }
        }

        private async void MonthDetailDay_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as UIElement).Properties;
            // 严格仅响应鼠标左键点击，彻底屏蔽右键、中键、前进后退侧键的误触发
            if (!props.IsLeftButtonPressed)
            {
                return;
            }
            e.Handled = true;

            var dayItem = (e.OriginalSource as FrameworkElement)?.DataContext as MonthCalendarDayItem
                          ?? (sender as FrameworkElement)?.DataContext as MonthCalendarDayItem;
            if (dayItem != null)
            {
                try
                {
                    var loc = LocalizationService.Instance;
                    var detail = await ViewModel.GetDayDetailAsync(dayItem.Date);

                    var rootPanel = new Grid { MinWidth = 460, MaxWidth = 520, RowSpacing = 16 };
                    rootPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    rootPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    rootPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    // 1. 顶部日期与汇总胶囊卡片
                    var headerCard = new Border
                    {
                        Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                        BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(16, 12, 16, 12)
                    };
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var dateStack = new StackPanel { Spacing = 2 };
                    dateStack.Children.Add(new TextBlock
                    {
                        Text = $"{dayItem.Date:yyyy-MM-dd}",
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    });
                    dateStack.Children.Add(new TextBlock
                    {
                        Text = loc.GetString("TipMiddleClickCopy", "提示：鼠标中键点击任意单词可直接复制"),
                        FontSize = 11,
                        Opacity = 0.5
                    });
                    Grid.SetColumn(dateStack, 0);
                    headerGrid.Children.Add(dateStack);

                    var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                    // 复习统计
                    var reviewBadge = new Border
                    {
                        Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    reviewBadge.Child = new TextBlock
                    {
                        Text = string.Format(loc.GetString("DetailReviewCountFormat", "复习 {0} 词"), detail.ReviewCount),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium
                    };
                    badgeStack.Children.Add(reviewBadge);

                    // 入库统计
                    var importBadge = new Border
                    {
                        Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    importBadge.Child = new TextBlock
                    {
                        Text = string.Format(loc.GetString("DetailImportCountFormat", "收录 {0} 词"), detail.ImportedCount),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium
                    };
                    badgeStack.Children.Add(importBadge);

                    Grid.SetColumn(badgeStack, 1);
                    headerGrid.Children.Add(badgeStack);
                    headerCard.Child = headerGrid;
                    Grid.SetRow(headerCard, 0);
                    rootPanel.Children.Add(headerCard);

                    // 2. 双 Pivot 选项卡（复习打卡列表 / 当日收录新词）
                    var pivot = new Pivot { Margin = new Thickness(0, -8, 0, 0) };

                    // 页面 A: 复习打卡
                    var reviewPivotItem = new PivotItem
                    {
                        Header = $"{loc.GetString("DetailTabReview", "复习打卡")} ({detail.ReviewCount})"
                    };
                    if (detail.ReviewedItems.Count > 0)
                    {
                        var reviewScroll = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                        var reviewStack = new StackPanel { Spacing = 3 };
                        foreach (var rev in detail.ReviewedItems)
                        {
                            var g = new Grid
                            {
                                Padding = new Thickness(10, 8, 10, 8),
                                Margin = new Thickness(0, 1, 0, 1),
                                CornerRadius = new CornerRadius(4),
                                Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                            var t = new TextBlock { Text = rev.WordText, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetColumn(t, 0); g.Children.Add(t);

                            var rb = new Border
                            {
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(6, 2, 6, 2),
                                Margin = new Thickness(8, 0, 8, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            rb.Child = new TextBlock { Text = rev.RatingText, FontSize = 11 };
                            Grid.SetColumn(rb, 1); g.Children.Add(rb);

                            var tm = new TextBlock { Text = rev.ReviewTimeText, FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetColumn(tm, 2); g.Children.Add(tm);

                            // 中键点击复制
                            g.PointerPressed += (s, args) =>
                            {
                                if (args.GetCurrentPoint(s as UIElement).Properties.IsMiddleButtonPressed)
                                {
                                    args.Handled = true;
                                    ClipboardHelper.CopyText(rev.WordText);
                                }
                            };
                            reviewStack.Children.Add(g);
                        }
                        reviewScroll.Content = reviewStack;
                        reviewPivotItem.Content = reviewScroll;
                    }
                    else
                    {
                        reviewPivotItem.Content = new TextBlock
                        {
                            Text = loc.GetString("DetailNoReviewLogs", "当天无打卡复习记录"),
                            FontSize = 12,
                            Opacity = 0.5,
                            Margin = new Thickness(8, 20, 8, 20),
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                    }
                    pivot.Items.Add(reviewPivotItem);

                    // 页面 B: 收录新词
                    var importPivotItem = new PivotItem
                    {
                        Header = $"{loc.GetString("DetailTabImport", "收录新词")} ({detail.ImportedCount})"
                    };
                    if (detail.ImportedWords.Count > 0)
                    {
                        var importScroll = new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                        var importStack = new StackPanel { Spacing = 3 };
                        foreach (var w in detail.ImportedWords)
                        {
                            var g = new Grid
                            {
                                Padding = new Thickness(10, 8, 10, 8),
                                Margin = new Thickness(0, 1, 0, 1),
                                CornerRadius = new CornerRadius(4),
                                Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                            var t = new TextBlock { Text = w.Text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetColumn(t, 0); g.Children.Add(t);

                            var lb = new Border
                            {
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(6, 2, 6, 2),
                                VerticalAlignment = VerticalAlignment.Center,
                                Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
                            };
                            lb.Child = new TextBlock { Text = w.LearningStateText, FontSize = 11 };
                            Grid.SetColumn(lb, 1); g.Children.Add(lb);

                            // 中键点击复制
                            g.PointerPressed += (s, args) =>
                            {
                                if (args.GetCurrentPoint(s as UIElement).Properties.IsMiddleButtonPressed)
                                {
                                    args.Handled = true;
                                    ClipboardHelper.CopyText(w.Text);
                                }
                            };
                            importStack.Children.Add(g);
                        }
                        importScroll.Content = importStack;
                        importPivotItem.Content = importScroll;
                    }
                    else
                    {
                        importPivotItem.Content = new TextBlock
                        {
                            Text = loc.GetString("DetailNoImportWords", "当天无新词收录入库"),
                            FontSize = 12,
                            Opacity = 0.5,
                            Margin = new Thickness(8, 20, 8, 20),
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                    }
                    pivot.Items.Add(importPivotItem);

                    Grid.SetRow(pivot, 1);
                    rootPanel.Children.Add(pivot);

                    var dialog = new ContentDialog
                    {
                        Title = loc.GetString("DialogTitleDayDetail", "打卡详情"),
                        Content = rootPanel,
                        CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                        XamlRoot = this.XamlRoot
                    };

                    MainWindow.RegisterActiveDialog(dialog);
                    await dialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(dialog);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "ActivityCalendarPage.MonthDetailDay_PointerPressed");
                }
            }
        }
    }
}
