using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using NihongoVocab.Converters;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 全局通用单词记忆与复习曲线详情弹窗辅助器。
    /// 支持在任何页面（记忆分析、活动日历、词库等）调起，并支持安全嵌套恢复父级 ContentDialog。
    /// </summary>
    public static class WordDetailDialogHelper
    {
        private static bool IsDarkTheme() => ThemeHelper.IsCurrentDarkTheme();

        private static Brush GetThemeCardBrush(bool isDark) =>
            isDark ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 43, 43))
                   : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));

        private static Brush GetThemeCardSecondaryBrush(bool isDark) =>
            isDark ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 50))
                   : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));

        private static Brush GetThemeLayerBrush(bool isDark) =>
            isDark ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38))
                   : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));

        private static Brush GetThemeStrokeBrush(bool isDark) =>
            isDark ? new SolidColorBrush(Windows.UI.Color.FromArgb(32, 255, 255, 255))
                   : new SolidColorBrush(Windows.UI.Color.FromArgb(22, 0, 0, 0));

        public static async Task ShowWordDetailDialogAsync(
            XamlRoot xamlRoot,
            Word word,
            ContentDialog? parentDialogToRestore = null)
        {
            if (word == null || xamlRoot == null) return;

            // 若存在正在展示的父级 Dialog，先安全暂隐，避开 WinUI 3 单一 ContentDialog 冲突限制
            if (parentDialogToRestore != null)
            {
                try
                {
                    parentDialogToRestore.Hide();
                }
                catch
                {
                }
                await Task.Yield();
            }

            try
            {
                var db = App.GetService<DatabaseService>();
                var logs = await db.GetReviewLogsByWordIdAsync(word.Id);
                bool isDark = IsDarkTheme();
                var currentTheme = (App.MainWindowInstance?.Content as FrameworkElement)?.ActualTheme 
                                   ?? (isDark ? ElementTheme.Dark : ElementTheme.Light);

                var root = new StackPanel { MinWidth = 540, MaxWidth = 580, Spacing = 16, RequestedTheme = currentTheme };

                // 1. 顶部单词信息与状态徽章
                var headerCard = new Border
                {
                    Background = GetThemeCardBrush(isDark),
                    BorderBrush = GetThemeStrokeBrush(isDark),
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
                        ClipboardHelper.CopyText(word.Text);
                    }
                };
                ToolTipService.SetToolTip(wordRow, loc.GetString("TipMiddleClickCopy", "提示：鼠标中键点击任意单词可直接复制"));

                var stateBadge = new Border
                {
                    Background = GetThemeCardSecondaryBrush(isDark),
                    BorderBrush = GetThemeStrokeBrush(isDark),
                    BorderThickness = new Thickness(1),
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
                var stabBox = CreateMetricBox(loc.GetString("MetricStabilityLabel", "记忆稳定性 (半衰期)"),
                    word.Stability <= 0 ? loc.GetString("MetricNotEvaluated", "未评测") : string.Format(loc.GetString("StabilityDaysFormat", "{0:F1} 天"), word.Stability), isDark);
                Grid.SetRow(stabBox, 0); Grid.SetColumn(stabBox, 0);
                metricsGrid.Children.Add(stabBox);

                // 难度评级
                var diffBox = CreateMetricBox(loc.GetString("MetricDifficultyLabel", "单词记忆难度"),
                    word.Difficulty <= 0 ? loc.GetString("MetricNotEvaluated", "未评测") : $"{word.Difficulty:F1} / 10", isDark);
                Grid.SetRow(diffBox, 0); Grid.SetColumn(diffBox, 1);
                metricsGrid.Children.Add(diffBox);

                // 复习统计
                var repBox = CreateMetricBox(loc.GetString("MetricReviewStatsLabel", "复习统计"),
                    string.Format(loc.GetString("ReviewStatsFormat", "已复习 {0} 次 / 遗忘 {1} 次"), word.Reps, word.Lapses), isDark);
                Grid.SetRow(repBox, 1); Grid.SetColumn(repBox, 0);
                metricsGrid.Children.Add(repBox);

                // 下次复习
                var nextBox = CreateMetricBox(loc.GetString("NextReviewLabel", "下次复习"), word.NextReviewIntervalText, isDark);
                Grid.SetRow(nextBox, 1); Grid.SetColumn(nextBox, 1);
                metricsGrid.Children.Add(nextBox);

                headerStack.Children.Add(metricsGrid);
                headerCard.Child = headerStack;
                root.Children.Add(headerCard);

                // 2. 单词记忆曲线
                var retentionCurveCard = CreateRetentionCurveCard(word, logs, loc, isDark);
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
                            Background = GetThemeLayerBrush(isDark),
                            BorderBrush = GetThemeStrokeBrush(isDark),
                            BorderThickness = new Thickness(1),
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
                                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94))
                                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
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

                var detailDialog = new ContentDialog
                {
                    Title = loc.GetString("DialogTitleWordDetail", "单词记忆与复习详情"),
                    Content = root,
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = xamlRoot,
                    RequestedTheme = currentTheme
                };
                detailDialog.Resources["ContentDialogMinWidth"] = 580.0;
                detailDialog.Resources["ContentDialogMaxWidth"] = 640.0;

                MainWindow.RegisterActiveDialog(detailDialog);
                await detailDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(detailDialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WordDetailDialogHelper.ShowWordDetailDialogAsync");
            }
            finally
            {
                // 若此前有父级 Dialog，在详情 Dialog 关闭后重新平滑唤起父级 Dialog
                if (parentDialogToRestore != null)
                {
                    await Task.Yield();
                    try
                    {
                        MainWindow.RegisterActiveDialog(parentDialogToRestore);
                        await parentDialogToRestore.ShowAsync();
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "WordDetailDialogHelper.RestoreParentDialog");
                    }
                    finally
                    {
                        MainWindow.UnregisterActiveDialog(parentDialogToRestore);
                    }
                }
            }
        }

        private static FrameworkElement CreateRetentionCurveCard(Word word, List<ReviewLog> logs, LocalizationService loc, bool isDark)
        {
            var card = new Border
            {
                Background = GetThemeCardBrush(isDark),
                BorderBrush = GetThemeStrokeBrush(isDark),
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
            bool isMastered = word.State == (int)WordLearningState.Mastered;
            bool isUnlearned = !isMastered && word.Reps <= 0 && word.Stability <= 0 && (logs == null || logs.Count == 0);
            double curStability = word.Stability > 0 ? word.Stability : 1.0;
            DateTime lastRevDate = word.LastReviewDate ?? (logs != null && logs.Count > 0 ? logs.Max(l => l.ReviewDate) : word.CreatedAt);
            double currentR = isMastered
                ? 1.0
                : (isUnlearned
                    ? 0.0
                    : FsrsEngine.CalculateRetrievability(curStability, lastRevDate, now));
            currentR = Math.Clamp(currentR, 0.0, 1.0);

            // 胶囊背景与文字颜色
            Windows.UI.Color badgeBgColor;
            Windows.UI.Color badgeFgColor;
            if (isUnlearned)
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 156, 163, 175);
                badgeFgColor = Windows.UI.Color.FromArgb(255, 156, 163, 175);
            }
            else if (currentR >= 0.90)
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 16, 185, 129);
                badgeFgColor = Windows.UI.Color.FromArgb(255, 16, 185, 129);
            }
            else if (currentR >= 0.70)
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 59, 130, 246);
                badgeFgColor = Windows.UI.Color.FromArgb(255, 59, 130, 246);
            }
            else
            {
                badgeBgColor = Windows.UI.Color.FromArgb(38, 239, 68, 68);
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

            // 2. 图例栏 (Legend) - 全新升级的高对比度现代科技配色
            var legendPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var historyLineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 56, 189, 248));   // 亮青天蓝 (Sky-400)
            var forecastLineBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 129, 140, 248)); // 亮靛紫蓝 (Indigo-400)

            // 图例：历史复习轨迹 (实线)
            var historyLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            historyLegend.Children.Add(new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = historyLineBrush,
                StrokeThickness = 2.6
            });
            historyLegend.Children.Add(new TextBlock { Text = loc.GetString("LegendHistory", "历史复习轨迹"), FontSize = 10, Opacity = 0.85 });
            legendPanel.Children.Add(historyLegend);

            // 图例：未来衰减预测 (虚线)
            var forecastLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var forecastLine = new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = forecastLineBrush,
                StrokeThickness = 2.2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                Opacity = 0.95
            };
            forecastLegend.Children.Add(forecastLine);
            forecastLegend.Children.Add(new TextBlock { Text = loc.GetString("LegendForecast", "未来记忆衰减预测"), FontSize = 10, Opacity = 0.85 });
            legendPanel.Children.Add(forecastLegend);

            // 图例：目标阈值 90% (金色虚线)
            var targetLegend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            targetLegend.Children.Add(new Line
            {
                X1 = 0, Y1 = 6, X2 = 16, Y2 = 6,
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });
            targetLegend.Children.Add(new TextBlock { Text = loc.GetString("TargetRetentionLineLabel", "目标阈值 90%"), FontSize = 10, Opacity = 0.75 });
            legendPanel.Children.Add(targetLegend);

            mainStack.Children.Add(legendPanel);

            // 3. 画布区域 (Canvas)
            double canvasWidth = 500.0;
            double canvasHeight = 150.0;
            double marginLeft = 38.0;
            double marginRight = 16.0;
            double marginTop = 12.0;
            double marginBottom = 24.0;

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

            double[] yTicks = { 1.0, 0.90, 0.70, 0.50, 0.0 };
            foreach (var r in yTicks)
            {
                double y = MapY(r);

                var tickText = new TextBlock
                {
                    Text = $"{r * 100:F0}%",
                    FontSize = 9,
                    Opacity = r == 0.90 ? 0.95 : 0.65,
                    FontWeight = r == 0.90 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Foreground = r == 0.90 ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)) : (isDark ? new SolidColorBrush(Microsoft.UI.Colors.White) : new SolidColorBrush(Microsoft.UI.Colors.Black)),
                    Width = 32,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(tickText, 2);
                Canvas.SetTop(tickText, y - 7);
                canvas.Children.Add(tickText);

                var hLine = new Line
                {
                    X1 = marginLeft,
                    Y1 = y,
                    X2 = marginLeft + plotWidth,
                    Y2 = y
                };

                if (Math.Abs(r - 0.90) < 0.001)
                {
                    hLine.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8));
                    hLine.StrokeThickness = 1.2;
                    hLine.StrokeDashArray = new DoubleCollection { 4, 3 };
                    hLine.Opacity = 0.85;

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
                    hLine.Stroke = isDark
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(35, 255, 255, 255))
                        : new SolidColorBrush(Windows.UI.Color.FromArgb(30, 0, 0, 0));
                    hLine.StrokeThickness = 1.0;
                    hLine.StrokeDashArray = new DoubleCollection { 2, 3 };
                    hLine.Opacity = 0.4;
                }
                canvas.Children.Add(hLine);
            }

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
                DateTime anchor = isUnlearned ? word.CreatedAt : lastRevDate;
                tStart = anchor <= now && (now - anchor).TotalDays < 180
                    ? anchor
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

            var historyPoints = new PointCollection();
            var dotsToRender = new List<(double X, double Y, ReviewLog Log)>();

            if (orderedLogs.Count == 0)
            {
                if (isMastered)
                {
                    historyPoints.Add(new Point(MapX(tStart), MapY(1.0)));
                    historyPoints.Add(new Point(MapX(now), MapY(1.0)));
                }
                else if (isUnlearned)
                {
                    historyPoints.Add(new Point(MapX(tStart), MapY(0.0)));
                    historyPoints.Add(new Point(MapX(now), MapY(0.0)));
                }
                else
                {
                    int segs = 12;
                    for (int s = 0; s <= segs; s++)
                    {
                        double frac = (double)s / segs;
                        DateTime t = tStart + TimeSpan.FromSeconds((now - tStart).TotalSeconds * frac);
                        double r = FsrsEngine.CalculateRetrievability(curStability, lastRevDate, t);
                        historyPoints.Add(new Point(MapX(t), MapY(r)));
                    }
                }
            }
            else
            {
                var firstLog = orderedLogs[0];
                if ((firstLog.ReviewDate - tStart).TotalMinutes > 5)
                {
                    if (firstLog.StabilityBefore > 0)
                    {
                        double preStab = firstLog.StabilityBefore;
                        int preSteps = 6;
                        for (int s = 0; s < preSteps; s++)
                        {
                            double frac = (double)s / preSteps;
                            DateTime t = tStart + TimeSpan.FromSeconds((firstLog.ReviewDate - tStart).TotalSeconds * frac);
                            double r = FsrsEngine.CalculateRetrievability(preStab, tStart, t);
                            historyPoints.Add(new Point(MapX(t), MapY(r)));
                        }
                    }
                    else
                    {
                        historyPoints.Add(new Point(MapX(tStart), MapY(0.0)));
                        historyPoints.Add(new Point(MapX(firstLog.ReviewDate), MapY(0.0)));
                    }
                }

                for (int i = 0; i < orderedLogs.Count; i++)
                {
                    var curLog = orderedLogs[i];
                    double xLog = MapX(curLog.ReviewDate);

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
                            double r = (isMastered && i == orderedLogs.Count - 1)
                                ? 1.0
                                : FsrsEngine.CalculateRetrievability(stab, curLog.ReviewDate, t);
                            historyPoints.Add(new Point(MapX(t), MapY(r)));
                        }
                    }
                }
            }

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
                gradBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(70, 56, 189, 248), Offset = 0.0 });
                gradBrush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, 56, 189, 248), Offset = 1.0 });
                shadowPolygon.Fill = gradBrush;
                canvas.Children.Add(shadowPolygon);

                var historyLine = new Polyline
                {
                    Points = historyPoints,
                    Stroke = historyLineBrush,
                    StrokeThickness = 2.6,
                    StrokeLineJoin = PenLineJoin.Round
                };
                canvas.Children.Add(historyLine);
            }

            var forecastPoints = new PointCollection();
            double xNow = MapX(now);
            forecastPoints.Add(new Point(xNow, MapY(currentR)));

            if (isMastered)
            {
                forecastPoints.Add(new Point(MapX(tEnd), MapY(1.0)));
            }
            else if (isUnlearned)
            {
                forecastPoints.Add(new Point(MapX(tEnd), MapY(0.0)));
            }
            else
            {
                int fSteps = 24;
                for (int s = 1; s <= fSteps; s++)
                {
                    double frac = (double)s / fSteps;
                    DateTime t = now + TimeSpan.FromSeconds((tEnd - now).TotalSeconds * frac);
                    double r = FsrsEngine.CalculateRetrievability(curStability, lastRevDate, t);
                    forecastPoints.Add(new Point(MapX(t), MapY(r)));
                }
            }

            var forecastPolyline = new Polyline
            {
                Points = forecastPoints,
                Stroke = forecastLineBrush,
                StrokeThickness = 2.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Opacity = 0.95
            };
            canvas.Children.Add(forecastPolyline);

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

            var todayLine = new Line
            {
                X1 = xNow,
                Y1 = MapY(1.0),
                X2 = xNow,
                Y2 = MapY(0.0),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 99, 102, 241)),
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Opacity = 0.65
            };
            canvas.Children.Add(todayLine);

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

            double xNext = MapX(tNext);
            if (!isMastered && !isUnlearned && xNext > xNow && xNext <= marginLeft + plotWidth)
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

            double yAxisLabel = canvasHeight - 16;

            var startLabel = new TextBlock
            {
                Text = $"{tStart:MM-dd} {loc.GetString("StartMarkerLabel", "首次导入")}",
                FontSize = 9,
                Opacity = 0.7,
                Foreground = isDark ? new SolidColorBrush(Microsoft.UI.Colors.White) : new SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            Canvas.SetLeft(startLabel, marginLeft);
            Canvas.SetTop(startLabel, yAxisLabel);
            canvas.Children.Add(startLabel);

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

            string nextMarkerText = isMastered
                ? loc.GetString("IntervalMastered", "已掌握 (免复习)")
                : (isUnlearned
                    ? loc.GetString("IntervalUnscheduled", "未安排 (待学习)")
                    : string.Format(loc.GetString("NextReviewMarkerFormat", "下次复习 ({0})"), $"{tNext:MM-dd}"));

            var nextLabel = new TextBlock
            {
                Text = nextMarkerText,
                FontSize = 9,
                Opacity = 0.85,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8))
            };
            Canvas.SetLeft(nextLabel, Math.Max(marginLeft + plotWidth - 95, xNow + 25));
            Canvas.SetTop(nextLabel, yAxisLabel);
            canvas.Children.Add(nextLabel);

            mainStack.Children.Add(canvas);

            if (isMastered)
            {
                mainStack.Children.Add(new TextBlock
                {
                    Text = loc.GetString("MasteredWordCurveTip", "该词已手动标记为【已掌握】，独立于 FSRS 遗忘调度流之外（免复习）"),
                    FontSize = 10,
                    Opacity = 0.55,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            else if (orderedLogs.Count == 0)
            {
                mainStack.Children.Add(new TextBlock
                {
                    Text = loc.GetString("NewWordNoReviewCurveTip", "该词尚未开始学习（当前记忆留存率 0%），完成首次复习打卡后将生成按日衰减曲线"),
                    FontSize = 10,
                    Opacity = 0.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }

            card.Child = mainStack;
            return card;
        }

        private static FrameworkElement CreateMetricBox(string label, string value, bool isDark)
        {
            var card = new Border
            {
                Background = GetThemeCardSecondaryBrush(isDark),
                BorderBrush = GetThemeStrokeBrush(isDark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8)
            };
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(new TextBlock { Text = label, FontSize = 10, Opacity = 0.6 });
            p.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            card.Child = p;
            return card;
        }
    }
}
