using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Models;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class StudyPage : Page
    {
        public StudyViewModel ViewModel => App.GetService<StudyViewModel>();

        private bool _isPageLoaded = false;

        public StudyPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

            BackgroundGrid.PointerPressed += BackgroundGrid_PointerPressed;
            ListFilterComboBox.SelectionChanged += ListFilterComboBox_SelectionChanged;
            CardsRepeater.PointerPressed += CardBorder_PointerPressed;
            CardsRepeater.ContextRequested += CardBorder_ContextRequested;

            this.Loaded += StudyPage_Loaded;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateLocalizedStrings);
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;

            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavStudy", "开始学习");
            if (ListFilterLabelTextBlock != null) ListFilterLabelTextBlock.Text = loc.GetString("LabelStudyScope", "学习范围:");
            if (ListFilterComboBox != null) ListFilterComboBox.PlaceholderText = loc.GetString("StudyStart_AllDueWordsPlaceholder", "全部到期词汇");
            if (PendingCountLabelTextBlock != null) PendingCountLabelTextBlock.Text = loc.GetString("LabelPendingStudy", "待学");
            if (CompletedCountLabelTextBlock != null) CompletedCountLabelTextBlock.Text = loc.GetString("LabelRemembered", "记得");
            if (LapsedCountLabelTextBlock != null) LapsedCountLabelTextBlock.Text = loc.GetString("LabelForgotten", "遗忘");

            if (SessionFinishedTitleTextBlock != null) SessionFinishedTitleTextBlock.Text = loc.GetString("MsgStudyFinishedTitle", "今日复习已全部完成！");
            if (SessionFinishedTipTextBlock != null) SessionFinishedTipTextBlock.Text = loc.GetString("MsgStudyFinishedTip", "目前没有需要学习或复习的单词。");

            if (ImmersiveButton != null)
            {
                ToolTipService.SetToolTip(ImmersiveButton, _isImmersive
                    ? loc.GetString("TooltipExitImmersiveMode", "退出沉浸式模式")
                    : loc.GetString("TooltipImmersiveMode", "沉浸式模式"));
            }
        }

        private bool _isImmersive = false;

        private void ImmersiveButton_Click(object sender, RoutedEventArgs e)
        {
            SetImmersiveMode(!_isImmersive);
        }

        private void SetImmersiveMode(bool enable)
        {
            _isImmersive = enable;
            var loc = LocalizationService.Instance;
            var mainWindow = App.MainWindowInstance as MainWindow;

            if (_isImmersive)
            {
                if (TopTitlePanel != null) TopTitlePanel.Visibility = Visibility.Collapsed;
                if (FilterControlPanel != null) FilterControlPanel.Visibility = Visibility.Collapsed;
                if (ImmersiveIcon != null) ImmersiveIcon.Glyph = "\uE73F";
                if (ImmersiveButton != null) ToolTipService.SetToolTip(ImmersiveButton, loc.GetString("TooltipExitImmersiveMode", "退出沉浸式模式"));
                mainWindow?.SetImmersiveMode(true);
            }
            else
            {
                if (TopTitlePanel != null) TopTitlePanel.Visibility = Visibility.Visible;
                if (FilterControlPanel != null) FilterControlPanel.Visibility = Visibility.Visible;
                if (ImmersiveIcon != null) ImmersiveIcon.Glyph = "\uE740";
                if (ImmersiveButton != null) ToolTipService.SetToolTip(ImmersiveButton, loc.GetString("TooltipImmersiveMode", "沉浸式模式"));
                mainWindow?.SetImmersiveMode(false);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_isImmersive)
            {
                SetImmersiveMode(false);
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                await ViewModel.EnsureAvailableListsAsync();

                if (ViewModel.Cards.Count > 0 || ViewModel.CompletedCount > 0 || ViewModel.LapsedCount > 0)
                {
                    await ViewModel.SyncSessionQueueAsync();
                }
                else
                {
                    await ViewModel.LoadSessionAsync(ViewModel.SelectedList?.Id);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyPage.OnNavigatedTo");
            }
        }

        private void StudyPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
        }

        private void ListFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPageLoaded) return;
            try
            {
                var newlySelected = (e.AddedItems?.Count > 0 ? e.AddedItems[0] as WordList : null)
                                    ?? ListFilterComboBox.SelectedItem as WordList;
                if (newlySelected != null && ViewModel.SelectedList?.Id != newlySelected.Id)
                {
                    ViewModel.SelectedList = newlySelected;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyPage.ListFilterComboBox_SelectionChanged");
            }
        }

        private void CardBorder_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            // 严格拦截并标记 Handled = true，彻底禁用系统上下文菜单，防止截断连续右键操作
            args.Handled = true;
        }

        private async void CardBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                e.Handled = true; // 关键：截断事件冒泡，绝对不传给父级 BackgroundGrid！
                var pointerPoint = e.GetCurrentPoint(sender as UIElement);

                var sourceElem = e.OriginalSource as FrameworkElement;
                var card = sourceElem?.DataContext as StudyCardItem ?? sourceElem?.Tag as StudyCardItem ?? (sender as FrameworkElement)?.Tag as StudyCardItem;
                if (card != null)
                {
                    if (pointerPoint.Properties.IsMiddleButtonPressed)
                    {
                        ClipboardHelper.CopyText(card.Word.Text);
                    }
                    else if (pointerPoint.Properties.IsRightButtonPressed)
                    {
                        await ViewModel.OnCardRightClickAsync(card);
                    }
                    else if (pointerPoint.Properties.IsLeftButtonPressed)
                    {
                        await ViewModel.OnCardLeftClickAsync(card);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyPage.CardBorder_PointerPressed");
            }
        }

        private void BackgroundGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                // 仅当点击源为背景本身时才重置待确认预选卡片
                if (e.OriginalSource == sender)
                {
                    ViewModel.ResetOtherPending();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "StudyPage.BackgroundGrid_PointerPressed");
            }
        }
    }
}
