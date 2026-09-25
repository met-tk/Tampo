using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace NihongoVocab.Views
{
    public sealed partial class MemoryAnalyticsPage : Page
    {
        public MemoryAnalyticsViewModel ViewModel { get; }

        public MemoryAnalyticsPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            ViewModel = App.GetService<MemoryAnalyticsViewModel>();
            this.DataContext = ViewModel;

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            this.Loaded += (s, e) => UpdateLocalizedStrings();
            UpdateLocalizedStrings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateLocalizedStrings);
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;

            string tooltipBrowseStateWords = loc.GetString("MemoryAnalytics_TooltipBrowseStateWords", "点击在全部词单中查看该状态下的全部单词");
            if (BorderStateNew != null) ToolTipService.SetToolTip(BorderStateNew, tooltipBrowseStateWords);
            if (BorderStateLearning != null) ToolTipService.SetToolTip(BorderStateLearning, tooltipBrowseStateWords);
            if (BorderStateReview != null) ToolTipService.SetToolTip(BorderStateReview, tooltipBrowseStateWords);
            if (BorderStateRelearning != null) ToolTipService.SetToolTip(BorderStateRelearning, tooltipBrowseStateWords);

            if (SearchWordCurveBox != null) SearchWordCurveBox.PlaceholderText = loc.GetString("MemoryAnalytics_SearchPlaceholder", "搜索单词分析记忆曲线...");
        }

        private void OnDatabaseDataChanged()
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ViewModel.RefreshStatsAsync();
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
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsPage.OnNavigatedTo");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
        }

        private async void WordItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is Word word)
            {
                var props = e.GetCurrentPoint(elem).Properties;
                if (props.IsMiddleButtonPressed)
                {
                    e.Handled = true;
                    CopyWordToClipboard(word.Text);
                    return;
                }

                if (props.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    await ShowWordDetailDialogAsync(word);
                }
            }
        }

        private async void StateCard_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var props = e.GetCurrentPoint(sender as UIElement).Properties;
            if (!props.IsLeftButtonPressed) return;
            e.Handled = true;

            if (sender is FrameworkElement elem && elem.Tag is string tagStr && int.TryParse(tagStr, out int stateInt))
            {
                var loc = LocalizationService.Instance;
                string stateName = stateInt switch
                {
                    0 => loc.GetString("StateNew", "未学习 (New)"),
                    1 => loc.GetString("StateLearning", "初学中 (Learning)"),
                    2 => loc.GetString("StateReview", "复习中 (Review)"),
                    3 => loc.GetString("StateRelearning", "重新学习 (Relearning)"),
                    _ => loc.GetString("StateScheduling", "调度状态")
                };

                await ShowStateWordsDialogAsync(stateInt, stateName);
            }
        }

        private async Task ShowStateWordsDialogAsync(int state, string stateName)
        {
            try
            {
                var loc = LocalizationService.Instance;
                var allWords = await ViewModel.GetWordsByStateAsync(state);

                var root = new Grid { MinWidth = 480, MaxWidth = 540, RowSpacing = 12 };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // 搜索过滤栏
                var searchBox = new AutoSuggestBox
                {
                    PlaceholderText = string.Format(loc.GetString("SearchInWordCountFormat", "在 {0} 个单词中搜索..."), allWords.Count),
                    QueryIcon = new SymbolIcon(Symbol.Find),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                Grid.SetRow(searchBox, 0);
                root.Children.Add(searchBox);

                var scrollViewer = new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var itemsControl = new ItemsControl();

                void RefreshList(string filter)
                {
                    var filtered = string.IsNullOrWhiteSpace(filter)
                        ? allWords
                        : allWords.Where(w => w.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                    itemsControl.Items.Clear();
                    if (filtered.Count == 0)
                    {
                        itemsControl.Items.Add(new TextBlock
                        {
                            Text = loc.GetString("NoMatchingWords", "无匹配单词"),
                            FontSize = 12,
                            Opacity = 0.5,
                            Margin = new Thickness(12, 24, 12, 24),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        return;
                    }

                    foreach (var w in filtered)
                    {
                        var border = new Border
                        {
                            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(12, 8, 12, 8),
                            Margin = new Thickness(0, 2, 0, 2),
                            Tag = w
                        };
                        ToolTipService.SetToolTip(border, loc.GetString("TipViewWordCurveAndMiddleCopy", "左键查看学习记录与记忆曲线，中键复制"));

                        var grid = new Grid();
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var wordText = new TextBlock { Text = w.Text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
                        Grid.SetColumn(wordText, 0);
                        grid.Children.Add(wordText);

                        var retrievabilityText = new TextBlock
                        {
                            Text = w.Stability <= 0 ? loc.GetString("NotRecalled", "未召回") : string.Format(loc.GetString("RetentionRateFormat", "留存率 {0}%"), (int)(w.CurrentRetrievability * 100)),
                            FontSize = 11,
                            Opacity = 0.7,
                            Margin = new Thickness(12, 0, 12, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(retrievabilityText, 1);
                        grid.Children.Add(retrievabilityText);

                        var nextText = new TextBlock
                        {
                            Text = w.NextReviewIntervalText,
                            FontSize = 11,
                            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                            Opacity = 0.85,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(nextText, 2);
                        grid.Children.Add(nextText);

                        border.Child = grid;

                        border.PointerPressed += async (s, e) =>
                        {
                            if (s is Border b && b.Tag is Word targetWord)
                            {
                                var ptProps = e.GetCurrentPoint(b).Properties;
                                if (ptProps.IsMiddleButtonPressed)
                                {
                                    e.Handled = true;
                                    CopyWordToClipboard(targetWord.Text);
                                    return;
                                }
                                if (ptProps.IsLeftButtonPressed)
                                {
                                    e.Handled = true;
                                    await ShowWordDetailDialogAsync(targetWord);
                                }
                            }
                        };

                        itemsControl.Items.Add(border);
                    }
                }

                searchBox.TextChanged += (s, args) => RefreshList(searchBox.Text);
                RefreshList(string.Empty);

                scrollViewer.Content = itemsControl;
                Grid.SetRow(scrollViewer, 1);
                root.Children.Add(scrollViewer);

                var dialog = new ContentDialog
                {
                    Title = string.Format(loc.GetString("DialogTitleStateWordsCountFormat", "{0} · 包含 {1} 词"), stateName, allWords.Count),
                    Content = root,
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = this.XamlRoot
                };

                MainWindow.RegisterActiveDialog(dialog);
                await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsPage.ShowStateWordsDialogAsync");
            }
        }

        private async Task ShowWordDetailDialogAsync(Word word)
        {
            try
            {
                var logs = await ViewModel.GetWordReviewLogsAsync(word.Id);

                var root = new StackPanel { MinWidth = 540, MaxWidth = 580, Spacing = 16 };

                // 1. 顶部单词信息与状态徽章
                var headerCard = new Border
                {
                    Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                    BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 14, 16, 14)
                };
                var headerStack = new StackPanel { Spacing = 8 };

                var loc = LocalizationService.Instance;
                var wordRow = new Grid();
                wordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                wordRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var wordText = new TextBlock
                {
                    Text = word.Text,
                    FontSize = 22,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                Grid.SetColumn(wordText, 0);
                wordRow.Children.Add(wordText);

                // 中键点击复制
                wordRow.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(s as UIElement).Properties.IsMiddleButtonPressed)
                    {
                        e.Handled = true;
                        CopyWordToClipboard(word.Text);
                    }
                };
                ToolTipService.SetToolTip(wordRow, loc.GetString("TipMiddleClickCopy", "提示：鼠标中键点击任意单词可直接复制"));

                var stateBadge = new Border
                {
                    Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 4)
                };
                stateBadge.Child = new TextBlock
                {
                    Text = word.LearningStateText,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium
                };
                Grid.SetColumn(stateBadge, 1);
                wordRow.Children.Add(stateBadge);
                headerStack.Children.Add(wordRow);

                // 4 核心指标网格
                var metricsGrid = new Grid { Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 12, RowSpacing = 8 };
                metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                metricsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                metricsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // 稳定性 / 半衰期
                var stabBox = CreateMetricBox(loc.GetString("MetricStabilityLabel", "记忆稳定性 (半衰期)"), word.Stability <= 0 ? loc.GetString("MetricNotEvaluated", "未评测") : string.Format(loc.GetString("StabilityDaysFormat", "{0:F1} 天"), word.Stability));
                Grid.SetRow(stabBox, 0); Grid.SetColumn(stabBox, 0);
                metricsGrid.Children.Add(stabBox);

                // 难度评级
                var diffBox = CreateMetricBox(loc.GetString("MetricDifficultyLabel", "单词记忆难度"), word.Difficulty <= 0 ? loc.GetString("MetricNotEvaluated", "未评测") : $"{word.Difficulty:F1} / 10");
                Grid.SetRow(diffBox, 0); Grid.SetColumn(diffBox, 1);
                metricsGrid.Children.Add(diffBox);

                // 复习次数与遗忘
                var repBox = CreateMetricBox(loc.GetString("MetricReviewStatsLabel", "复习统计"), string.Format(loc.GetString("ReviewStatsFormat", "已复习 {0} 次 / 遗忘 {1} 次"), word.Reps, word.Lapses));
                Grid.SetRow(repBox, 1); Grid.SetColumn(repBox, 0);
                metricsGrid.Children.Add(repBox);

                // 下次复习
                var nextBox = CreateMetricBox(loc.GetString("NextReviewLabel", "下次复习"), word.NextReviewIntervalText);
                Grid.SetRow(nextBox, 1); Grid.SetColumn(nextBox, 1);
                metricsGrid.Children.Add(nextBox);

                headerStack.Children.Add(metricsGrid);
                headerCard.Child = headerStack;
                root.Children.Add(headerCard);

                // 2. 单词记忆曲线（纵坐标记忆程度，横坐标学习进度，参考 anki_fsrs_visualizer）
                var retentionCurveCard = CreateRetentionCurveCard(word, logs, loc);
                root.Children.Add(retentionCurveCard);

                // 3. 学习与打卡履历记录
                var logSection = new StackPanel { Spacing = 8 };
                logSection.Children.Add(new TextBlock
                {
                    Text = string.Format(loc.GetString("ReviewTimelineFormat", "学习与复习记录时间轴 ({0} 条)"), logs.Count),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Opacity = 0.8
                });

                if (logs.Count > 0)
                {
                    var logScroll = new ScrollViewer { MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    var logList = new StackPanel { Spacing = 4 };
                    foreach (var l in logs)
                    {
                        var logItem = new Grid
                        {
                            Background = Application.Current.Resources["LayerFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(10, 6, 10, 6)
                        };
                        logItem.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        logItem.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var dateText = new TextBlock
                        {
                            Text = l.ReviewDate.ToString("yyyy-MM-dd HH:mm:ss"),
                            FontSize = 11,
                            Opacity = 0.75,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(dateText, 0);
                        logItem.Children.Add(dateText);

                        var ratingText = new TextBlock
                        {
                            Text = l.Rating == 3 ? loc.GetString("RatingRemembered", "记得") : loc.GetString("RatingForgotten", "遗忘"),
                            FontSize = 11,
                            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                            Foreground = l.Rating == 3
                                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94))
                                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(ratingText, 1);
                        logItem.Children.Add(ratingText);

                        logList.Children.Add(logItem);
                    }
                    logScroll.Content = logList;
                    logSection.Children.Add(logScroll);
                }
                else
                {
                    logSection.Children.Add(new TextBlock
                    {
                        Text = loc.GetString("NoReviewLogsYet", "该单词尚未建立复习打卡日志"),
                        FontSize = 11,
                        Opacity = 0.5,
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                }
                root.Children.Add(logSection);

                var dialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleWordDetail", "单词记忆与复习详情"),
                    Content = root,
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = this.XamlRoot
                };
                dialog.Resources["ContentDialogMinWidth"] = 580.0;
                dialog.Resources["ContentDialogMaxWidth"] = 640.0;

                MainWindow.RegisterActiveDialog(dialog);
                await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MemoryAnalyticsPage.ShowWordDetailDialogAsync");
            }
        }

        private FrameworkElement CreateRetentionCurveCard(Word word, List<ReviewLog> logs, LocalizationService loc)
        {
            var card = new Border
            {
                Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var mainStack = new StackPanel { Spacing = 10 };

            // 1. 顶部标题栏与当前留存率胶囊
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Spacing = 2 };
            titleStack.Children.Add(new TextBlock
            {
                Text = loc.GetString("RetentionCurveTitle", "记忆程度与学习进度曲线 (FSRS)"),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = loc.GetString("RetentionCurveDesc", "纵轴为记忆留存度 (0-100%)，横轴为时间与打卡推进进度"),
                FontSize = 10,
                Opacity = 0.6
            });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            // 当前留存率估算
            DateTime now = DateTime.Now;
            double curStability = word.Stability > 0 ? word.Stability : 1.0;
            DateTime lastRevDate = word.LastReviewDate ?? (logs != null && logs.Count > 0 ? logs.Max(l => l.ReviewDate) : word.CreatedAt);
            double currentR = word.Stability <= 0 && (logs == null || logs.Count == 0)
                ? 1.0
                : FsrsEngine.CalculateRetrievability(curStability, lastRevDate, now);
            currentR = Math.Clamp(currentR, 0.0, 1.0);

            // 胶囊背景与文字颜色
            Windows.UI.Color badgeBgColor;
            Windows.UI.Color badgeFgColor;
            if (currentR >= 0.90)
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 16, 185, 129); // 绿色半透
                badgeFgColor = Windows.UI.Color.FromArgb(255, 16, 185, 129);
            }
            else if (currentR >= 0.70)
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 59, 130, 246); // 蓝色半透
                badgeFgColor = Windows.UI.Color.FromArgb(255, 59, 130, 246);
            }
            else
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 239, 68, 68); // 红色半透
                badgeFgColor = Windows.UI.Color.FromArgb(255, 239, 68, 68);
            }

            var retentionBadge = new Border
            {
                Background = new SolidColorBrush(badgeBgColor),
                BorderBrush = new SolidColorBrush(badgeFgColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            retentionBadge.Child = new TextBlock
            {
                Text = $"{loc.GetString("CurrentRetentionLabel", "当前记忆程度")}: {currentR * 100:F1}%",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(badgeFgColor)
            };
            Grid.SetColumn(retentionBadge, 1);
            headerGrid.Children.Add(retentionBadge);
            mainStack.Children.Add(headerGrid);

            // 2. 图例栏 (Legend)
            var legendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(0, 0, 0, 2)
            };

            // 图例：历史复习轨迹 (实线)
            var historyLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            historyLegend.Children.Add(new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = Application.Current.Resources["AccentFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)),
                StrokeThickness = 2.5
            });
            historyLegend.Children.Add(new TextBlock { Text = loc.GetString("LegendHistory", "历史复习轨迹"), FontSize = 10, Opacity = 0.75 });
            legendPanel.Children.Add(historyLegend);

            // 图例：未来衰减预测 (虚线)
            var forecastLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var forecastLine = new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = Application.Current.Resources["AccentFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)),
                StrokeThickness = 2.0,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Opacity = 0.8
            };
            forecastLegend.Children.Add(forecastLine);
            forecastLegend.Children.Add(new TextBlock { Text = loc.GetString("LegendForecast", "未来记忆衰减预测"), FontSize = 10, Opacity = 0.75 });
            legendPanel.Children.Add(forecastLegend);

            // 图例：目标阈值 90% (金色虚线)
            var targetLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            targetLegend.Children.Add(new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)), // 金黄色
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });
            targetLegend.Children.Add(new TextBlock { Text = loc.GetString("TargetRetentionLineLabel", "目标阈值 90%"), FontSize = 10, Opacity = 0.75 });
            legendPanel.Children.Add(targetLegend);

            mainStack.Children.Add(legendPanel);

            // 3. 画布区域 (Canvas)
            double canvasWidth = 500.0;
            double canvasHeight = 150.0;
            double marginLeft = 38.0;   // Y轴文字宽度
            double marginRight = 16.0;
            double marginTop = 12.0;    // R = 1.0 (100%)
            double marginBottom = 24.0; // X轴时间标签留白

            double plotWidth = canvasWidth - marginLeft - marginRight;
            double plotHeight = canvasHeight - marginTop - marginBottom;

            double MapY(double r)
            {
                r = Math.Clamp(r, 0.0, 1.0);
                return marginTop + (1.0 - r) * plotHeight;
            }

            var canvas = new Canvas
            {
                Width = canvasWidth,
                Height = canvasHeight,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Y轴刻度定义: 100%, 90%, 70%, 50%, 0%
            double[] yTicks = { 1.0, 0.90, 0.70, 0.50, 0.0 };
            foreach (var r in yTicks)
            {
                double y = MapY(r);

                // 刻度文字
                var tickText = new TextBlock
                {
                    Text = $"{r * 100:F0}%",
                    FontSize = 9,
                    Opacity = r == 0.90 ? 0.9 : 0.45,
                    FontWeight = r == 0.90 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = r == 0.90 ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)) : null,
                    Width = 32,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(tickText, 2);
                Canvas.SetTop(tickText, y - 7);
                canvas.Children.Add(tickText);

                // 水平网格线
                var hLine = new Line
                {
                    X1 = marginLeft,
                    Y1 = y,
                    X2 = marginLeft + plotWidth,
                    Y2 = y
                };

                if (Math.Abs(r - 0.90) < 0.001)
                {
                    // 90% 期望目标线 (金黄色虚线)
                    hLine.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8));
                    hLine.StrokeThickness = 1.2;
                    hLine.StrokeDashArray = new DoubleCollection { 4, 3 };
                    hLine.Opacity = 0.85;

                    // 90% 右端微标签
                    var targetTag = new TextBlock
                    {
                        Text = "90%",
                        FontSize = 8,
                        Opacity = 0.8,
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8))
                    };
                    Canvas.SetLeft(targetTag, marginLeft + plotWidth - 24);
                    Canvas.SetTop(targetTag, y - 11);
                    canvas.Children.Add(targetTag);
                }
                else
                {
                    hLine.Stroke = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush
                                   ?? new SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128));
                    hLine.StrokeThickness = 1.0;
                    hLine.StrokeDashArray = new DoubleCollection { 2, 3 };
                    hLine.Opacity = 0.35;
                }
                canvas.Children.Add(hLine);
            }

            // 计算时间轴范围
            var orderedLogs = (logs ?? new List<ReviewLog>()).OrderBy(l => l.ReviewDate).ToList();
            DateTime tStart;
            if (orderedLogs.Count > 0)
            {
                var firstLog = orderedLogs[0];
                tStart = word.CreatedAt < firstLog.ReviewDate && (firstLog.ReviewDate - word.CreatedAt).TotalDays < 180
                    ? word.CreatedAt
                    : firstLog.ReviewDate.AddHours(-2);
            }
            else
            {
                tStart = word.CreatedAt <= now && (now - word.CreatedAt).TotalDays < 180
                    ? word.CreatedAt
                    : now.AddDays(-1);
            }

            DateTime tNext;
            if (word.NextReviewDate.HasValue && word.NextReviewDate.Value > now)
            {
                tNext = word.NextReviewDate.Value;
            }
            else
            {
                int nextDays = FsrsEngine.NextIntervalDays(curStability);
                tNext = now.AddDays(Math.Max(1, nextDays));
            }

            // 终点设置在 tNext 之后留出少量边距
            DateTime tEnd = tNext.AddDays(Math.Max(1.0, (tNext - now).TotalDays * 0.25));
            if ((tEnd - tStart).TotalDays < 3.0)
            {
                tEnd = tStart.AddDays(3.0);
            }

            double totalSeconds = (tEnd - tStart).TotalSeconds;
            if (totalSeconds <= 0) totalSeconds = 86400.0;

            double MapX(DateTime t)
            {
                double sec = (t - tStart).TotalSeconds;
                double ratio = Math.Clamp(sec / totalSeconds, 0.0, 1.0);
                return marginLeft + ratio * plotWidth;
            }

            // 4. 生成历史曲线与阴影区域
            var historyPoints = new PointCollection();
            var dotsToRender = new List<(double X, double Y, ReviewLog Log)>();

            var accentBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush
                              ?? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246));

            if (orderedLogs.Count == 0)
            {
                // 无复习打卡日志：初次学习模拟线（从 tStart 的 100% 衰减到 now 的 currentR）
                int segs = 12;
                for (int s = 0; s <= segs; s++)
                {
                    double frac = (double)s / segs;
                    DateTime t = tStart + TimeSpan.FromSeconds((now - tStart).TotalSeconds * frac);
                    double r = FsrsEngine.CalculateRetrievability(curStability, tStart, t);
                    historyPoints.Add(new Point(MapX(t), MapY(r)));
                }
            }
            else
            {
                // 有复习打卡日志：构建复习跃迁与衰减锯齿曲线
                var firstLog = orderedLogs[0];
                if ((firstLog.ReviewDate - tStart).TotalMinutes > 5)
                {
                    double preStab = firstLog.StabilityBefore > 0 ? firstLog.StabilityBefore : 1.0;
                    int preSteps = 6;
                    for (int s = 0; s < preSteps; s++)
                    {
                        double frac = (double)s / preSteps;
                        DateTime t = tStart + TimeSpan.FromSeconds((firstLog.ReviewDate - tStart).TotalSeconds * frac);
                        double r = FsrsEngine.CalculateRetrievability(preStab, tStart, t);
                        historyPoints.Add(new Point(MapX(t), MapY(r)));
                    }
                }

                for (int i = 0; i < orderedLogs.Count; i++)
                {
                    var curLog = orderedLogs[i];
                    double xLog = MapX(curLog.ReviewDate);

                    // 打卡时刻瞬间留存率拉升至 100% (1.0)
                    historyPoints.Add(new Point(xLog, MapY(1.0)));
                    dotsToRender.Add((xLog, MapY(1.0), curLog));

                    DateTime nextTime = (i + 1 < orderedLogs.Count) ? orderedLogs[i + 1].ReviewDate : now;
                    double stab = curLog.StabilityAfter > 0 ? curLog.StabilityAfter : 1.0;

                    if (nextTime > curLog.ReviewDate)
                    {
                        int steps = Math.Clamp((int)((nextTime - curLog.ReviewDate).TotalDays * 2), 6, 20);
                        for (int s = 1; s <= steps; s++)
                        {
                            double frac = (double)s / steps;
                            DateTime t = curLog.ReviewDate + TimeSpan.FromSeconds((nextTime - curLog.ReviewDate).TotalSeconds * frac);
                            double r = FsrsEngine.CalculateRetrievability(stab, curLog.ReviewDate, t);
                            historyPoints.Add(new Point(MapX(t), MapY(r)));
                        }
                    }
                }
            }

            // 历史渐变阴影面 (Polygon)
            if (historyPoints.Count > 1)
            {
                var shadowPolygon = new Polygon();
                var polyPoints = new PointCollection();
                polyPoints.Add(new Point(historyPoints[0].X, MapY(0.0)));
                foreach (var pt in historyPoints)
                {
                    polyPoints.Add(pt);
                }
                polyPoints.Add(new Point(historyPoints[historyPoints.Count - 1].X, MapY(0.0)));
                shadowPolygon.Points = polyPoints;

                var gradBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                gradBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(50, 59, 130, 246), Offset = 0.0 });
                gradBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 59, 130, 246), Offset = 1.0 });
                shadowPolygon.Fill = gradBrush;
                canvas.Children.Add(shadowPolygon);

                // 历史实线 (Polyline)
                var historyLine = new Polyline
                {
                    Points = historyPoints,
                    Stroke = accentBrush,
                    StrokeThickness = 2.4,
                    StrokeLineJoin = PenLineJoin.Round
                };
                canvas.Children.Add(historyLine);
            }

            // 5. 生成未来预测衰减曲线 (虚线)
            var forecastPoints = new PointCollection();
            double xNow = MapX(now);
            forecastPoints.Add(new Point(xNow, MapY(currentR)));

            int fSteps = 24;
            for (int s = 1; s <= fSteps; s++)
            {
                double frac = (double)s / fSteps;
                DateTime t = now + TimeSpan.FromSeconds((tEnd - now).TotalSeconds * frac);
                double r = FsrsEngine.CalculateRetrievability(curStability, lastRevDate, t);
                forecastPoints.Add(new Point(MapX(t), MapY(r)));
            }

            var forecastPolyline = new Polyline
            {
                Points = forecastPoints,
                Stroke = accentBrush,
                StrokeThickness = 2.0,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Opacity = 0.8
            };
            canvas.Children.Add(forecastPolyline);

            // 6. 绘制历史打卡圆点 (Dots)
            foreach (var dotInfo in dotsToRender)
            {
                bool isGood = dotInfo.Log.Rating == 3;
                var dotColor = isGood ? Windows.UI.Color.FromArgb(255, 16, 185, 129) : Windows.UI.Color.FromArgb(255, 239, 68, 68);

                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(dotColor),
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                    StrokeThickness = 1.5
                };
                Canvas.SetLeft(dot, dotInfo.X - 4);
                Canvas.SetTop(dot, dotInfo.Y - 4);

                string ratingStr = isGood ? loc.GetString("RatingRemembered", "记得") : loc.GetString("RatingForgotten", "遗忘");
                string tip = $"{dotInfo.Log.ReviewDate:yyyy-MM-dd HH:mm}\n{ratingStr}\nS: {dotInfo.Log.StabilityBefore:F1}d → {dotInfo.Log.StabilityAfter:F1}d";
                ToolTipService.SetToolTip(dot, tip);

                canvas.Children.Add(dot);
            }

            // 7. 今日垂直指示线与当前标记
            var todayLine = new Line
            {
                X1 = xNow,
                Y1 = MapY(1.0),
                X2 = xNow,
                Y2 = MapY(0.0),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 99, 102, 241)), // 紫色
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Opacity = 0.65
            };
            canvas.Children.Add(todayLine);

            // 当前留存率指示点外环 + 内核
            var curDotOuter = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 99, 102, 241)),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 99, 102, 241)),
                StrokeThickness = 1.5
            };
            Canvas.SetLeft(curDotOuter, xNow - 5);
            Canvas.SetTop(curDotOuter, MapY(currentR) - 5);
            canvas.Children.Add(curDotOuter);

            var curDotInner = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            };
            Canvas.SetLeft(curDotInner, xNow - 2);
            Canvas.SetTop(curDotInner, MapY(currentR) - 2);
            canvas.Children.Add(curDotInner);
            ToolTipService.SetToolTip(curDotOuter, $"{loc.GetString("TodayMarkerLabel", "今日")}: {currentR * 100:F1}%");

            // 8. 下次复习推荐点标记 (Next Review Marker)
            double xNext = MapX(tNext);
            if (xNext > xNow && xNext <= marginLeft + plotWidth)
            {
                var nextLine = new Line
                {
                    X1 = xNext,
                    Y1 = MapY(0.95),
                    X2 = xNext,
                    Y2 = MapY(0.0),
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)),
                    StrokeThickness = 1.0,
                    StrokeDashArray = new DoubleCollection { 1, 2 },
                    Opacity = 0.5
                };
                canvas.Children.Add(nextLine);
            }

            // 9. X轴时间轴底部标签 (Start / Today / Next)
            double yAxisLabel = canvasHeight - 16;

            // 起始标签
            var startLabel = new TextBlock
            {
                Text = $"{tStart:MM-dd} {loc.GetString("StartMarkerLabel", "首次导入")}",
                FontSize = 9,
                Opacity = 0.55
            };
            Canvas.SetLeft(startLabel, marginLeft);
            Canvas.SetTop(startLabel, yAxisLabel);
            canvas.Children.Add(startLabel);

            // 今日标签
            var todayLabel = new TextBlock
            {
                Text = loc.GetString("TodayMarkerLabel", "今日"),
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 99, 102, 241)),
                Opacity = 0.9
            };
            Canvas.SetLeft(todayLabel, Math.Clamp(xNow - 10, marginLeft + 40, marginLeft + plotWidth - 80));
            Canvas.SetTop(todayLabel, yAxisLabel);
            canvas.Children.Add(todayLabel);

            // 下次复习标签
            var nextLabel = new TextBlock
            {
                Text = string.Format(loc.GetString("NextReviewMarkerFormat", "下次复习 ({0})"), $"{tNext:MM-dd}"),
                FontSize = 9,
                Opacity = 0.65,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8))
            };
            Canvas.SetLeft(nextLabel, Math.Max(marginLeft + plotWidth - 85, xNow + 25));
            Canvas.SetTop(nextLabel, yAxisLabel);
            canvas.Children.Add(nextLabel);

            mainStack.Children.Add(canvas);

            // 如果暂无打卡日志，显示一条轻量提示
            if (orderedLogs.Count == 0)
            {
                mainStack.Children.Add(new TextBlock
                {
                    Text = loc.GetString("NewWordNoReviewCurveTip", "该词尚未开始打卡复习，当前展示初次学习后的理论记忆衰减模拟曲线"),
                    FontSize = 10,
                    Opacity = 0.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            card.Child = mainStack;
            return card;
        }

        private FrameworkElement CreateMetricBox(string label, string value)
        {
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(new TextBlock { Text = label, FontSize = 10, Opacity = 0.5 });
            p.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            return p;
        }

        private void CopyWordToClipboard(string text)
        {
            ClipboardHelper.CopyText(text);
        }
    }
}
