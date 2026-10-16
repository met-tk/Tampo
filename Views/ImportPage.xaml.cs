using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;
using NihongoVocab.Views.Dialogs;

namespace NihongoVocab.Views
{
    public sealed partial class ImportPage : Page
    {
        public ImportViewModel ViewModel => App.GetService<ImportViewModel>();

        public ImportPage()
        {
            this.InitializeComponent();
            this.Loaded += (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
                UpdateLocalizedStrings();
            };
            this.Unloaded += (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            };
            UpdateLocalizedStrings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateLocalizedStrings);
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;
            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavImport", "导入词汇");
            if (PresetLabelTextBlock != null) PresetLabelTextBlock.Text = loc.GetString("LabelPreset", "文本清洗预设:");
            if (ManagePresetsButtonText != null) ManagePresetsButtonText.Text = loc.GetString("ButtonManagePresets", "管理清洗预设");
            if (ImportButtonText != null) ImportButtonText.Text = loc.GetString("ButtonImportToVault", "导入至词库");
            if (RawTextPromptTextBlock != null) RawTextPromptTextBlock.Text = loc.GetString("PromptRawText", "在此粘贴或输入需要提取的日语原文（支持批量长文输入）：");
            if (RawTextBox != null) RawTextBox.PlaceholderText = loc.GetString("Import_OriginalTextPlaceholder", "输入或粘贴文本...");
            if (CandidatesPromptTextBlock != null) CandidatesPromptTextBlock.Text = loc.GetString("PromptCandidates", "清洗并提取出的候选词（鼠标悬浮高亮在原文中的位置，点击可移除单个）：");
            if (ExtractedCountLabelTextBlock != null) ExtractedCountLabelTextBlock.Text = loc.GetString("LabelExtractedCount", "提取总数:");

            if (ImmersiveButton != null)
            {
                ToolTipService.SetToolTip(ImmersiveButton, _isImmersive
                    ? loc.GetString("TooltipExitImmersiveMode", "退出沉浸式模式")
                    : loc.GetString("TooltipImmersiveMode", "沉浸式模式"));
            }
            UpdatePinTopUI();
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
                if (PresetControlPanel != null) PresetControlPanel.Visibility = Visibility.Collapsed;
                if (RawTextPromptTextBlock != null) RawTextPromptTextBlock.Visibility = Visibility.Collapsed;
                if (CandidatesPromptTextBlock != null) CandidatesPromptTextBlock.Visibility = Visibility.Collapsed;
                if (PinTopButton != null) PinTopButton.Visibility = Visibility.Visible;
                if (PageRootGrid != null) PageRootGrid.Padding = new Thickness(14, 10, 14, 10);

                if (ImmersiveIcon != null) ImmersiveIcon.Glyph = "\uE73F";
                if (ImmersiveButton != null) ToolTipService.SetToolTip(ImmersiveButton, loc.GetString("TooltipExitImmersiveMode", "退出沉浸式模式"));

                if (MainContentGrid != null && Col0 != null && Col1 != null && Row0 != null && Row1 != null)
                {
                    Col0.Width = new GridLength(1, GridUnitType.Star);
                    Col1.Width = new GridLength(0);
                    Row0.Height = new GridLength(160);
                    Row1.Height = new GridLength(1, GridUnitType.Star);

                    Grid.SetRow(RawTextContainer, 0);
                    Grid.SetColumn(RawTextContainer, 0);

                    Grid.SetRow(CandidatesContainer, 1);
                    Grid.SetColumn(CandidatesContainer, 0);
                }

                mainWindow?.SetImmersiveMode(true, compactWindow: true);
                UpdatePinTopUI();
            }
            else
            {
                if (TopTitlePanel != null) TopTitlePanel.Visibility = Visibility.Visible;
                if (PresetControlPanel != null) PresetControlPanel.Visibility = Visibility.Visible;
                if (RawTextPromptTextBlock != null) RawTextPromptTextBlock.Visibility = Visibility.Visible;
                if (CandidatesPromptTextBlock != null) CandidatesPromptTextBlock.Visibility = Visibility.Visible;
                if (PinTopButton != null) PinTopButton.Visibility = Visibility.Collapsed;
                if (PageRootGrid != null) PageRootGrid.Padding = new Thickness(24);

                if (ImmersiveIcon != null) ImmersiveIcon.Glyph = "\uE740";
                if (ImmersiveButton != null) ToolTipService.SetToolTip(ImmersiveButton, loc.GetString("TooltipImmersiveMode", "沉浸式模式"));

                if (MainContentGrid != null && Col0 != null && Col1 != null && Row0 != null && Row1 != null)
                {
                    Col0.Width = new GridLength(1, GridUnitType.Star);
                    Col1.Width = new GridLength(1, GridUnitType.Star);
                    Row0.Height = new GridLength(1, GridUnitType.Star);
                    Row1.Height = GridLength.Auto;

                    Grid.SetRow(RawTextContainer, 0);
                    Grid.SetColumn(RawTextContainer, 0);

                    Grid.SetRow(CandidatesContainer, 0);
                    Grid.SetColumn(CandidatesContainer, 1);
                }

                mainWindow?.SetImmersiveMode(false);
                mainWindow?.SetAlwaysOnTop(false);
            }
        }

        private void PinTopButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = App.MainWindowInstance as MainWindow;
            mainWindow?.ToggleAlwaysOnTop();
            UpdatePinTopUI();
        }

        private void UpdatePinTopUI()
        {
            var loc = LocalizationService.Instance;
            var mainWindow = App.MainWindowInstance as MainWindow;
            bool isTop = mainWindow?.IsAlwaysOnTop ?? false;

            if (PinTopIcon != null)
            {
                PinTopIcon.Glyph = isTop ? "\uE77A" : "\uE840";
            }
            if (PinTopButton != null)
            {
                ToolTipService.SetToolTip(PinTopButton, isTop
                    ? loc.GetString("TooltipUnpinTop", "取消置顶")
                    : loc.GetString("TooltipAlwaysOnTop", "窗口置顶"));
            }
        }

        protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_isImmersive)
            {
                SetImmersiveMode(false);
            }
        }

        private async void ManagePresetsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var presetManager = App.GetService<Services.PresetManager>();
                var dialog = new ManagePresetsDialog(presetManager)
                {
                    XamlRoot = this.XamlRoot
                };

                MainWindow.RegisterActiveDialog(dialog);
                await dialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(dialog);

                ViewModel.ReloadPresets();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ImportPage.ManagePresetsButton_Click");
            }
        }

        private void DeleteCandidate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string word)
            {
                ViewModel.RemoveCandidateWord(word);
            }
        }

        private void CandidateChip_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement elem && elem.Tag is string word && !string.IsNullOrEmpty(word))
                {
                    string fullText = RawTextBox.Text;
                    if (!string.IsNullOrEmpty(fullText))
                    {
                        int idx = fullText.IndexOf(word, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            RawTextBox.Select(idx, word.Length);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ImportPage.CandidateChip_PointerEntered");
            }
        }

        private void CandidateChip_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                RawTextBox.Select(0, 0);
            }
            catch
            {
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationService.Instance;
            if (ViewModel.CleanedWords.Count == 0)
            {
                var tipDialog = new ContentDialog
                {
                    Title = loc.GetString("NoCandidatesDialogTitle", "无候选词"),
                    Content = DialogHelper.CreateTextBlockContent(loc.GetString("NoCandidatesDialogContent", "当前右侧预览中没有清洗出的有效候选词，请先输入原始文本或调整清洗预设。")),
                    CloseButtonText = loc.GetString("ButtonConfirm", "确定"),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };
                MainWindow.RegisterActiveDialog(tipDialog);
                await tipDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(tipDialog);
                return;
            }

            try
            {
                var res = await ViewModel.ImportWordsAsync();
                if (res != null)
                {
                    string summaryFmt = loc.GetString("ImportSummaryFormat", "导入统计完成：\n• 成功新增入库：{0} 词\n• 查重跳过已有词：{1} 词");
                    string msg = string.Format(summaryFmt, res.InsertedCount, res.DuplicatedCount);
                    if (res.DuplicatedSamples.Count > 0)
                    {
                        string dupFmt = loc.GetString("DuplicateSamplesFormat", "\n（跳过样本：{0} 等）");
                        msg += string.Format(dupFmt, string.Join(", ", res.DuplicatedSamples));
                    }

                    var successDialog = new ContentDialog
                    {
                        Title = loc.GetString("ImportDoneDialogTitle", "导入完成"),
                        Content = DialogHelper.CreateTextBlockContent(msg),
                        CloseButtonText = loc.GetString("ButtonConfirm", "确定"),
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };
                    MainWindow.RegisterActiveDialog(successDialog);
                    await successDialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(successDialog);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ImportPage.ImportButton_Click");
            }
        }
    }
}
