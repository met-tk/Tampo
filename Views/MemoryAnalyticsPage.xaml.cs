using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class MemoryAnalyticsPage : Page
    {
        public MemoryAnalyticsViewModel ViewModel { get; }
        private readonly DataRefreshGate _dataRefreshGate = new(TimeSpan.FromMinutes(1));

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
                if (ReferenceEquals(this.Frame?.Content, this))
                {
                    await _dataRefreshGate.RefreshIfNeededAsync(ViewModel.LoadDataAsync);
                }
            });
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
            DatabaseService.DataChanged += OnDatabaseDataChanged;

            try
            {
                await _dataRefreshGate.RefreshIfNeededAsync(ViewModel.LoadDataAsync);
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
                    await WordDetailDialogHelper.ShowWordDetailDialogAsync(this.XamlRoot, word);
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

        private static bool IsDarkTheme() => Converters.ThemeHelper.IsCurrentDarkTheme();

        private static readonly Brush DarkLayerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
        private static readonly Brush LightLayerBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));
        private static readonly Brush DarkStrokeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 255, 255, 255));
        private static readonly Brush LightStrokeBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(22, 0, 0, 0));

        private static Brush GetThemeLayerBrush(bool isDark) => isDark ? DarkLayerBrush : LightLayerBrush;
        private static Brush GetThemeStrokeBrush(bool isDark) => isDark ? DarkStrokeBrush : LightStrokeBrush;

        private async Task ShowStateWordsDialogAsync(int state, string stateName)
        {
            try
            {
                var loc = LocalizationService.Instance;
                bool isDark = IsDarkTheme();
                var currentTheme = (App.MainWindowInstance?.Content as FrameworkElement)?.ActualTheme ?? this.ActualTheme;
                var allWords = await ViewModel.GetWordsByStateAsync(state);

                var root = new Grid { MinWidth = 480, MaxWidth = 540, RowSpacing = 12, RequestedTheme = currentTheme };
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

                ContentDialog dialog = null!;

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
                            Background = GetThemeLayerBrush(isDark),
                            BorderBrush = GetThemeStrokeBrush(isDark),
                            BorderThickness = new Thickness(1),
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
                                    await WordDetailDialogHelper.ShowWordDetailDialogAsync(this.XamlRoot, targetWord, dialog);
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

                dialog = new ContentDialog
                {
                    Title = string.Format(loc.GetString("DialogTitleStateWordsCountFormat", "{0} · 包含 {1} 词"), stateName, allWords.Count),
                    Content = root,
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = currentTheme
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

        private void CopyWordToClipboard(string text)
        {
            ClipboardHelper.CopyText(text);
        }
    }
}
