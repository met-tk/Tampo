using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class TodayStudyPage : Page
    {
        public TodayStudyViewModel ViewModel { get; }

        public TodayStudyPage()
        {
            ViewModel = App.GetService<TodayStudyViewModel>();
            this.DataContext = ViewModel;
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            RestorePreferences();
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

            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavTodayStudy", "今日学习");
            if (StatusFilterLabelTextBlock != null) StatusFilterLabelTextBlock.Text = loc.GetString("LabelStateFilter", "状态筛选:");
            if (TotalTodayLabelTextBlock != null) TotalTodayLabelTextBlock.Text = loc.GetString("LabelTodayStudy", "今日学习:");
            if (RememberLabelTextBlock != null) RememberLabelTextBlock.Text = loc.GetString("LabelRemembered", "记得:");
            if (ForgetLabelTextBlock != null) ForgetLabelTextBlock.Text = loc.GetString("LabelForgotten", "遗忘:");
            if (GridViewButtonText != null) GridViewButtonText.Text = loc.GetString("ViewModeGrid", "网格");
            if (ListViewButtonText != null) ListViewButtonText.Text = loc.GetString("ViewModeList", "列表");
            if (RefreshButtonText != null) RefreshButtonText.Text = loc.GetString("ButtonRefresh", "刷新");

            if (StatusFilterComboBox != null)
            {
                int curStatusIdx = StatusFilterComboBox.SelectedIndex;
                foreach (ComboBoxItem item in StatusFilterComboBox.Items)
                {
                    if (item.Tag is string tag)
                    {
                        item.Content = tag switch
                        {
                            "All" => loc.GetString("StatusTierAll", "全部打卡"),
                            "Remember" => loc.GetString("StatusTierRemember", "记得 (Good)"),
                            "Forget" => loc.GetString("StatusTierForget", "遗忘 (Again)"),
                            _ => item.Content
                        };
                    }
                }
                if (curStatusIdx >= 0)
                {
                    StatusFilterComboBox.SelectedIndex = -1;
                    StatusFilterComboBox.SelectedIndex = curStatusIdx;
                }
            }
        }

        private void RestorePreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;

                // 1. 恢复状态筛选下拉
                string savedFilter = pref.Get("TodayStudy_StatusFilter", "All");
                foreach (ComboBoxItem item in StatusFilterComboBox.Items)
                {
                    if (item.Tag?.ToString() == savedFilter)
                    {
                        StatusFilterComboBox.SelectedItem = item;
                        if (ViewModel != null) ViewModel.StatusFilter = savedFilter;
                        break;
                    }
                }

                // 2. 恢复网格/列表视图模式
                string savedMode = pref.Get("TodayStudy_ViewMode", "Grid");
                if (ViewModel != null)
                {
                    ViewModel.ViewMode = savedMode;
                }
                UpdateViewModeUI();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyPage.RestorePreferences");
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
                        UpdateViewModeUI();
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyPage.OnDatabaseDataChanged");
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
                    UpdateViewModeUI();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TodayStudyPage.OnNavigatedTo");
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

        private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel == null) return;
            if (StatusFilterComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ViewModel.StatusFilter = tag;
                UserPreferenceService.Instance.Set("TodayStudy_StatusFilter", tag);
            }
        }

        private void GridViewButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ViewMode = "Grid";
            UserPreferenceService.Instance.Set("TodayStudy_ViewMode", "Grid");
            UpdateViewModeUI();
        }

        private void ListViewButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ViewMode = "List";
            UserPreferenceService.Instance.Set("TodayStudy_ViewMode", "List");
            UpdateViewModeUI();
        }

        private void UpdateViewModeUI()
        {
            if (ViewModel.ViewMode == "Grid")
            {
                GridScrollViewer.Visibility = Visibility.Visible;
                RecordsListView.Visibility = Visibility.Collapsed;
            }
            else
            {
                GridScrollViewer.Visibility = Visibility.Collapsed;
                RecordsListView.Visibility = Visibility.Visible;
            }
        }

        // 卡片与条目指针事件：中键复制单词，网格视图下左键点击直接反转切换状态
        private async void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var ptr = e.GetCurrentPoint(sender as UIElement);
            if (sender is FrameworkElement elem && elem.DataContext is TodayWordCardItem item)
            {
                if (ptr.Properties.IsMiddleButtonPressed)
                {
                    ClipboardHelper.CopyText(item.WordText);
                    e.Handled = true;
                    return;
                }

                if (ptr.Properties.IsLeftButtonPressed && ViewModel.ViewMode == "Grid")
                {
                    e.Handled = true;
                    await ViewModel.ToggleItemRatingAsync(item);
                }
            }
        }

        // 列表视图：点击按钮切换状态
        private async void ListItemToggleRating_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TodayWordCardItem item)
            {
                await ViewModel.ToggleItemRatingAsync(item);
            }
        }

        // 列表视图：撤销记录
        private async void ListItemRevert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TodayWordCardItem item)
            {
                await ViewModel.RevertItemAsync(item);
            }
        }
    }
}
