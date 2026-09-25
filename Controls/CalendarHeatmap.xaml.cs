using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using NihongoVocab.Services;

namespace NihongoVocab.Controls
{
    public sealed partial class CalendarHeatmap : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(CalendarHeatmap),
                new PropertyMetadata("热力图", OnTitleChanged));

        public static readonly DependencyProperty HeatmapColorProperty =
            DependencyProperty.Register(nameof(HeatmapColor), typeof(Color), typeof(CalendarHeatmap),
                new PropertyMetadata(Color.FromArgb(255, 34, 197, 94), OnHeatmapColorChanged));

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(Dictionary<DateTime, int>), typeof(CalendarHeatmap),
                new PropertyMetadata(null, OnDataChanged));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public Color HeatmapColor
        {
            get => (Color)GetValue(HeatmapColorProperty);
            set => SetValue(HeatmapColorProperty, value);
        }

        public Dictionary<DateTime, int> Data
        {
            get => (Dictionary<DateTime, int>)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public CalendarHeatmap()
        {
            this.InitializeComponent();
            UpdateLegendColors();
            RenderGrid();
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarHeatmap control)
            {
                control.TitleTextBlock.Text = e.NewValue?.ToString() ?? string.Empty;
            }
        }

        private static void OnHeatmapColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarHeatmap control)
            {
                control.UpdateLegendColors();
                control.RenderGrid();
            }
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CalendarHeatmap control)
            {
                control.RenderGrid();
            }
        }

        private void UpdateLegendColors()
        {
            LegendLevel1.Background = new SolidColorBrush(GetColorForLevel(1));
            LegendLevel2.Background = new SolidColorBrush(GetColorForLevel(2));
            LegendLevel3.Background = new SolidColorBrush(GetColorForLevel(3));
            LegendLevel4.Background = new SolidColorBrush(GetColorForLevel(4));
        }

        private Color GetColorForLevel(int level)
        {
            byte a = level switch
            {
                1 => 60,
                2 => 120,
                3 => 190,
                4 => 255,
                _ => 15
            };
            return Color.FromArgb(a, HeatmapColor.R, HeatmapColor.G, HeatmapColor.B);
        }

        public void RenderGrid()
        {
            WeeksContainer.Children.Clear();

            // 生成过去 18 周（126天）的日期矩阵
            DateTime today = DateTime.Today;
            int dayOfWeek = (int)today.DayOfWeek; // 0=Sunday
            DateTime startDay = today.AddDays(-((17 * 7) + dayOfWeek));

            var dataDict = Data ?? new Dictionary<DateTime, int>();

            for (int w = 0; w < 18; w++)
            {
                var weekCol = new StackPanel { Spacing = 3 };

                for (int d = 0; d < 7; d++)
                {
                    DateTime currentDay = startDay.AddDays(w * 7 + d);
                    int count = 0;
                    dataDict.TryGetValue(currentDay.Date, out count);

                    int level = 0;
                    if (count > 0 && count <= 2) level = 1;
                    else if (count >= 3 && count <= 6) level = 2;
                    else if (count >= 7 && count <= 12) level = 3;
                    else if (count > 12) level = 4;

                    var cell = new Border
                    {
                        Width = 14,
                        Height = 14,
                        CornerRadius = new CornerRadius(2),
                        Background = level == 0
                            ? (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"]
                            : new SolidColorBrush(GetColorForLevel(level)),
                        Tag = currentDay.Date
                    };

                    ToolTipService.SetToolTip(cell, $"{currentDay:yyyy-MM-dd}: {count} 次");
                    cell.Tapped += OnCellTapped;
                    weekCol.Children.Add(cell);
                }

                WeeksContainer.Children.Add(weekCol);
            }
        }

        private async void OnCellTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement cell && cell.Tag is DateTime day)
            {
                try
                {
                    var db = App.GetService<DatabaseService>();
                    var detail = await db.GetDayDetailAsync(day);
                    ShowDetailFlyout(cell, detail);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "CalendarHeatmap.OnCellTapped");
                }
            }
        }

        private void ShowDetailFlyout(FrameworkElement target, DayActivityDetail detail)
        {
            var flyout = new Flyout();

            var rootPanel = new StackPanel
            {
                Spacing = 10,
                Width = 260
            };

            // 日期标题
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            headerPanel.Children.Add(new FontIcon
            {
                Glyph = "\uED28",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = detail.Date.ToString("yyyy-MM-dd"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            });
            rootPanel.Children.Add(headerPanel);

            // 分割线
            rootPanel.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
            });

            // 导入统计
            var importSection = new StackPanel { Spacing = 4 };
            var importHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            importHeader.Children.Add(new FontIcon { Glyph = "\uE8B7", FontSize = 12, Opacity = 0.8 });
            importHeader.Children.Add(new TextBlock
            {
                Text = $"导入词数：{detail.ImportedCount} 词",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            });
            importSection.Children.Add(importHeader);

            if (detail.SampleImportedWords.Count > 0)
            {
                var wordChipsPanel = new VariableSizedWrapGrid
                {
                    MaximumRowsOrColumns = 5,
                    Orientation = Orientation.Horizontal,
                    ItemWidth = double.NaN
                };

                // 流式标签
                var flowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                var wrapContainer = new ItemsControl();
                var wrapPanel = new StackPanel { Spacing = 4 };

                var tagsWrap = new TextBlock
                {
                    Text = string.Join("  •  ", detail.SampleImportedWords),
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(18, 0, 0, 0)
                };
                importSection.Children.Add(tagsWrap);
            }
            rootPanel.Children.Add(importSection);

            // 学习/复习统计
            var studySection = new StackPanel { Spacing = 4 };
            var studyHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            studyHeader.Children.Add(new FontIcon { Glyph = "\uE7BE", FontSize = 12, Opacity = 0.8 });
            studyHeader.Children.Add(new TextBlock
            {
                Text = $"复习总计：{detail.ReviewCount} 次",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            });
            studySection.Children.Add(studyHeader);

            var statBreakdown = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Margin = new Thickness(18, 0, 0, 0)
            };
            statBreakdown.Children.Add(new TextBlock
            {
                Text = $"记住：{detail.RememberCount}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94))
            });
            statBreakdown.Children.Add(new TextBlock
            {
                Text = $"遗忘：{detail.ForgetCount}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
            });
            studySection.Children.Add(statBreakdown);
            rootPanel.Children.Add(studySection);

            flyout.Content = rootPanel;
            flyout.ShowAt(target);
        }
    }
}
