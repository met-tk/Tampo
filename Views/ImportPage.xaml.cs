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
            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavImport", "导入词汇");
            if (PresetLabelTextBlock != null) PresetLabelTextBlock.Text = loc.GetString("LabelPreset", "文本清洗预设:");
            if (ManagePresetsButtonText != null) ManagePresetsButtonText.Text = loc.GetString("ButtonManagePresets", "管理清洗预设");
            if (ImportButtonText != null) ImportButtonText.Text = loc.GetString("ButtonImportToVault", "导入至词库");
            if (RawTextPromptTextBlock != null) RawTextPromptTextBlock.Text = loc.GetString("PromptRawText", "在此粘贴或输入需要提取的日语原文（支持批量长文输入）：");
            if (RawTextBox != null) RawTextBox.PlaceholderText = loc.GetString("Import_OriginalTextPlaceholder", "输入或粘贴文本...");
            if (CandidatesPromptTextBlock != null) CandidatesPromptTextBlock.Text = loc.GetString("PromptCandidates", "清洗并提取出的候选词（鼠标悬浮高亮在原文中的位置，点击可移除单个）：");
            if (ExtractedCountLabelTextBlock != null) ExtractedCountLabelTextBlock.Text = loc.GetString("LabelExtractedCount", "提取总数:");
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

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                    XamlRoot = this.XamlRoot
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
                        XamlRoot = this.XamlRoot
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
