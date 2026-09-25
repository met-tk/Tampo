using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class RecentStudyPage : Page
    {
        public RecentStudyViewModel ViewModel { get; }

        public RecentStudyPage()
        {
            ViewModel = App.GetService<RecentStudyViewModel>();
            this.DataContext = ViewModel;
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            RestorePreferences();
            TimeSpanComboBox.SelectionChanged += TimeSpanComboBox_SelectionChanged;
            StateFilterComboBox.SelectionChanged += StateFilterComboBox_SelectionChanged;
            RecentWordsSearchBox.TextChanged += RecentWordsSearchBox_TextChanged;
            RefreshButton.Click += RefreshButton_Click;
            SelectAllCheckBox.Checked += SelectAllCheckBox_Checked;
            SelectAllCheckBox.Unchecked += SelectAllCheckBox_Unchecked;
            RecentWordsListView.SelectionChanged += RecentWordsListView_SelectionChanged;
            RecentWordsListView.PointerReleased += RecentWordsListView_PointerReleased;
            MenuSetStateNewItem.Click += MenuSetStateNew_Click;
            MenuSetStateMasteredItem.Click += MenuSetStateMastered_Click;
            MenuReimportTodayItem.Click += MenuReimportToday_Click;
            MenuDeleteWordsItem.Click += MenuDeleteWords_Click;

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            UpdateLocalizedStrings();
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;

            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavRecentStudy", "最近学习");
            if (TimeSpanLabel != null) TimeSpanLabel.Text = loc.GetString("LabelTimeSpan", "时间跨度:");
            if (StateFilterLabel != null) StateFilterLabel.Text = loc.GetString("LabelStateFilter", "状态筛选:");
            if (TotalFoundLabel != null) TotalFoundLabel.Text = loc.GetString("LabelTotalFound", "共计查得:");
            if (RefreshButtonText != null) RefreshButtonText.Text = loc.GetString("ButtonRefresh", "刷新");
            if (RightClickHintText != null) RightClickHintText.Text = loc.GetString("RightClickActionHint", "右键任一项目或多选以唤出管理菜单");
            if (MenuSetStateNewItem != null) MenuSetStateNewItem.Text = loc.GetString("MenuSetStateNew", "标记为未学");
            if (MenuSetStateMasteredItem != null) MenuSetStateMasteredItem.Text = loc.GetString("MenuSetStateMastered", "标记为已掌握");
            if (MenuReimportTodayItem != null) MenuReimportTodayItem.Text = loc.GetString("MenuReimportToday", "加入今日学习");
            if (MenuDeleteWordsItem != null) MenuDeleteWordsItem.Text = loc.GetString("ButtonDelete", "从词库中移除");

            if (RecentWordsSearchBox != null) RecentWordsSearchBox.PlaceholderText = loc.GetString("RecentStudy_SearchPlaceholder", "在查得词汇中检索...");
            if (SelectAllCheckBox != null) SelectAllCheckBox.Content = loc.GetString("RecentStudy_SelectAll", "全选查得词汇");

            if (TimeSpanComboBox != null)
            {
                int curTimeIdx = TimeSpanComboBox.SelectedIndex;
                foreach (ComboBoxItem item in TimeSpanComboBox.Items)
                {
                    if (item.Tag is string tag)
                    {
                        item.Content = tag switch
                        {
                            "Day" => loc.GetString("TimeSpanDay", "日 (最近24h)"),
                            "Week" => loc.GetString("TimeSpanWeek", "周 (最近7天)"),
                            "Month" => loc.GetString("TimeSpanMonth", "月 (最近30天)"),
                            "Quarter" => loc.GetString("TimeSpanQuarter", "季度 (最近90天)"),
                            _ => item.Content
                        };
                    }
                }
                if (curTimeIdx >= 0)
                {
                    TimeSpanComboBox.SelectedIndex = -1;
                    TimeSpanComboBox.SelectedIndex = curTimeIdx;
                }
            }

            if (StateFilterComboBox != null)
            {
                int curStateIdx = StateFilterComboBox.SelectedIndex;
                foreach (ComboBoxItem item in StateFilterComboBox.Items)
                {
                    if (item.Tag is string tag)
                    {
                        item.Content = tag switch
                        {
                            "All" => loc.GetString("StateAll", "全部状态"),
                            "New" => loc.GetString("StateNew", "未学习 (New)"),
                            "Learning" => loc.GetString("StateLearning", "学习中 (Learning)"),
                            "Review" => loc.GetString("StateReview", "复习中 (Review)"),
                            "Mastered" => loc.GetString("StateMastered", "已掌握 (Mastered)"),
                            _ => item.Content
                        };
                    }
                }
                if (curStateIdx >= 0)
                {
                    StateFilterComboBox.SelectedIndex = -1;
                    StateFilterComboBox.SelectedIndex = curStateIdx;
                }
            }
        }

        private void RestorePreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;

                // 1. 恢复时间跨度下拉
                string savedSpan = pref.Get("RecentStudy_TimeSpan", "Week");
                foreach (ComboBoxItem item in TimeSpanComboBox.Items)
                {
                    if (item.Tag?.ToString() == savedSpan)
                    {
                        TimeSpanComboBox.SelectedItem = item;
                        if (ViewModel != null) ViewModel.TimeSpanLevel = savedSpan;
                        break;
                    }
                }

                // 2. 恢复状态筛选下拉
                string savedState = pref.Get("RecentStudy_StateFilter", "All");
                foreach (ComboBoxItem item in StateFilterComboBox.Items)
                {
                    if (item.Tag?.ToString() == savedState)
                    {
                        StateFilterComboBox.SelectedItem = item;
                        if (ViewModel != null) ViewModel.SelectedStateFilter = savedState;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "RecentStudyPage.RestorePreferences");
            }
        }

        private void OnDatabaseDataChanged()
        {
            try
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (ViewModel != null)
                    {
                        await ViewModel.LoadDataAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "RecentStudyPage.OnDatabaseDataChanged");
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UpdateLocalizedStrings();
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
            DatabaseService.DataChanged += OnDatabaseDataChanged;

            try
            {
                if (ViewModel != null)
                {
                    await ViewModel.LoadDataAsync();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "RecentStudyPage.OnNavigatedTo");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            DatabaseService.DataChanged -= OnDatabaseDataChanged;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                await ViewModel.LoadDataAsync();
            }
        }

        private void TimeSpanComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (TimeSpanComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.TimeSpanLevel = tag;
                UserPreferenceService.Instance.Set("RecentStudy_TimeSpan", tag);
            }
        }

        private void StateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (StateFilterComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.SelectedStateFilter = tag;
                UserPreferenceService.Instance.Set("RecentStudy_StateFilter", tag);
            }
        }

        private void RecentWordsSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (ViewModel != null)
            {
                ViewModel.SearchText = sender.Text.Trim();
            }
        }

        private void RecentWordsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var loc = LocalizationService.Instance;
            int count = RecentWordsListView.SelectedItems.Count;
            SelectedCountTextBlock.Text = string.Format(loc.GetString("SelectedCountFormat", "已选 {0} 项"), count);
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RecentWordsListView.SelectAll();
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RecentWordsListView.SelectedItems.Clear();
        }

        private async void MenuSetStateNew_Click(object sender, RoutedEventArgs e)
        {
            var selected = RecentWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.BatchUpdateStateAsync(selected, WordLearningState.New);
        }

        private async void MenuSetStateMastered_Click(object sender, RoutedEventArgs e)
        {
            var selected = RecentWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.BatchUpdateStateAsync(selected, WordLearningState.Mastered);
        }

        private async void MenuReimportToday_Click(object sender, RoutedEventArgs e)
        {
            var selected = RecentWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            await ViewModel.ReimportWordsTodayAsync(selected);
        }

        private async void MenuDeleteWords_Click(object sender, RoutedEventArgs e)
        {
            var selected = RecentWordsListView.SelectedItems.OfType<Word>().ToList();
            if (selected.Count == 0) return;

            var loc = LocalizationService.Instance;
            var dialog = new ContentDialog
            {
                Title = loc.GetString("DialogTitleDeleteWords", "删除所选单词"),
                Content = DialogHelper.CreateTextBlockContent(string.Format(loc.GetString("DialogMsgDeleteWordsFormat", "确定要永久删除所选的 {0} 个单词吗？\n此操作将同时删除相关复习记录且不可撤销。"), selected.Count)),
                PrimaryButtonText = loc.GetString("ButtonConfirmDelete", "确认删除"),
                CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteWordsAsync(selected);
            }
        }

        #region 滑动多选与反选手势交互 (Drag/Swipe-to-Select)
        private bool _isDragSelecting;
        private bool _dragSelectTargetState; // true = 正在批量勾选, false = 正在批量反选/取消勾选

        private void WordItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(sender as UIElement);
            if (sender is FrameworkElement fe && fe.DataContext is Word word)
            {
                if (ptr.Properties.IsMiddleButtonPressed)
                {
                    ClipboardHelper.CopyText(word.Text);
                    e.Handled = true;
                    return;
                }

                if (ptr.Properties.IsLeftButtonPressed)
                {
                    _isDragSelecting = true;
                    bool currentlySelected = RecentWordsListView.SelectedItems.Contains(word);
                    _dragSelectTargetState = !currentlySelected;

                    if (_dragSelectTargetState)
                    {
                        if (!RecentWordsListView.SelectedItems.Contains(word))
                        {
                            RecentWordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        RecentWordsListView.SelectedItems.Remove(word);
                    }
                    e.Handled = true;
                }
            }
        }

        private void WordItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragSelecting && e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                if (sender is FrameworkElement fe && fe.DataContext is Word word)
                {
                    if (_dragSelectTargetState)
                    {
                        if (!RecentWordsListView.SelectedItems.Contains(word))
                        {
                            RecentWordsListView.SelectedItems.Add(word);
                        }
                    }
                    else
                    {
                        RecentWordsListView.SelectedItems.Remove(word);
                    }
                }
            }
        }

        private void WordItem_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }

        private void RecentWordsListView_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragSelecting = false;
        }
        #endregion
    }
}
