using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace NihongoVocab.Views.Dialogs
{
    public sealed partial class FormatExportDialog : ContentDialog
    {
        private readonly ExportPresetManager _presetManager;
        private readonly List<Word> _words;

        public FormatExportDialog(IEnumerable<Word> words)
        {
            this.InitializeComponent();
            _presetManager = ExportPresetManager.Instance;
            _words = words?.ToList() ?? new List<Word>();

            UpdateLocalizedStrings();
            RefreshPresets();
            UpdatePreview();
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;
            this.Title = loc.GetString("FormatExportDialogTitle", "格式化导出 (Regex 替换)");
            this.CloseButtonText = loc.GetString("ButtonClose", "关闭");

            if (SavedRulesTitleText != null) SavedRulesTitleText.Text = loc.GetString("SavedExportRulesTitle", "已保存导出规则库");
            if (SavedRulesTipText != null) SavedRulesTipText.Text = loc.GetString("SavedRulesTip", "点击选择可带入右侧表单进行测试或编辑");
            if (ConfigRulesTitleText != null) ConfigRulesTitleText.Text = loc.GetString("ConfigExportRulesTitle", "自定义格式化导出规则");

            if (PresetNameInput != null)
            {
                PresetNameInput.Header = loc.GetString("PresetNameHeader", "预设名称");
                PresetNameInput.PlaceholderText = loc.GetString("PresetNamePlaceholder", "输入预设名称...");
            }
            if (PresetPatternInput != null)
            {
                PresetPatternInput.Header = loc.GetString("RegexPatternHeader", "正则表达式 (Pattern)");
                PresetPatternInput.PlaceholderText = loc.GetString("RegexPatternPlaceholder", "如：^(.*?)\\t?(.*?)\\t?(.*)$");
            }
            if (PresetReplacementInput != null)
            {
                PresetReplacementInput.Header = loc.GetString("RegexReplacementHeader", "替换表达式 (Replacement)");
                PresetReplacementInput.PlaceholderText = loc.GetString("RegexReplacementPlaceholder", "如：$1【$2】 $3");
            }
            if (AddPresetButtonText != null) AddPresetButtonText.Text = loc.GetString("ButtonSaveNewPreset", "保存为新预设");

            if (InstantVerifyTitleText != null) InstantVerifyTitleText.Text = loc.GetString("SingleLineTestTitle", "单行即时效果测试");
            if (TestLineInput != null) TestLineInput.PlaceholderText = loc.GetString("TestLinePlaceholderExport", "输入单行测试文本（如：日本語\\tにほんご\\t日语）...");
            if (TestResultTextBox != null) TestResultTextBox.PlaceholderText = loc.GetString("TestResultPlaceholder", "替换结果...");

            if (FullPreviewTitleText != null) FullPreviewTitleText.Text = loc.GetString("FullPreviewTitle", "所选单词导出预览");
            if (ExportTipText != null) ExportTipText.Text = loc.GetString("ExportTipRegex", "提示：支持标准 Regex 捕获组（$1, $2 等）与 \\t, \\n 等换行转义");
            if (CopyAllButtonText != null) CopyAllButtonText.Text = loc.GetString("ButtonCopyToClipboard", "复制到剪贴板");
            if (ExportFileButtonText != null) ExportFileButtonText.Text = loc.GetString("ButtonExportToFile", "导出为文件...");

            UpdateWordCountSummary();
        }

        private void UpdateWordCountSummary()
        {
            var loc = LocalizationService.Instance;
            if (SelectedCountSummaryText != null)
            {
                SelectedCountSummaryText.Text = string.Format(loc.GetString("ExportWordsCountSummaryFormat", "共计 {0} 个词汇"), _words.Count);
            }
        }

        private void RefreshPresets()
        {
            PresetsListView.ItemsSource = null;
            PresetsListView.ItemsSource = _presetManager.Presets;
            if (_presetManager.Presets.Count > 0)
            {
                PresetsListView.SelectedIndex = 0;
            }
        }

        private void PresetsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetsListView.SelectedItem is ExportPreset preset)
            {
                PresetNameInput.Text = preset.Name;
                PresetPatternInput.Text = preset.Pattern;
                PresetReplacementInput.Text = preset.Replacement;

                if (string.IsNullOrWhiteSpace(TestLineInput.Text))
                {
                    if (_words.Count > 0)
                    {
                        TestLineInput.Text = ExportPresetManager.FormatWordToSourceLine(_words[0]);
                    }
                    else
                    {
                        TestLineInput.Text = "日本語\tにほんご\t日语";
                    }
                }

                UpdatePreview();
            }
        }

        private void AddPreset_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationService.Instance;
            string name = PresetNameInput.Text.Trim();
            string pattern = PresetPatternInput.Text.Trim();
            string replacement = PresetReplacementInput.Text;

            if (string.IsNullOrWhiteSpace(name))
            {
                PresetNameInput.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                PresetPatternInput.Focus(FocusState.Programmatic);
                return;
            }

            var newPreset = new ExportPreset
            {
                Name = name,
                Pattern = pattern,
                Replacement = replacement,
                IsBuiltIn = false,
                Description = $"Regex: {pattern} -> {replacement}"
            };

            _presetManager.AddPreset(newPreset);
            RefreshPresets();
            PresetsListView.SelectedItem = newPreset;
        }

        private void ConfirmDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ExportPreset preset)
            {
                _presetManager.RemovePreset(preset);
                RefreshPresets();
            }
        }

        private void RuleInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            string pattern = PresetPatternInput?.Text ?? string.Empty;
            string replacement = PresetReplacementInput?.Text ?? string.Empty;

            // 1. 单行即时测试预览
            if (TestResultTextBox != null)
            {
                string testLine = TestLineInput?.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(testLine))
                {
                    TestResultTextBox.Text = ExportPresetManager.FormatLine(testLine, pattern, replacement);
                }
                else
                {
                    TestResultTextBox.Text = string.Empty;
                }
            }

            // 2. 全量所选词汇预览
            if (FullPreviewTextBox != null)
            {
                FullPreviewTextBox.Text = GenerateExportText(pattern, replacement);
            }
        }

        private string GenerateExportText(string pattern, string replacement)
        {
            if (_words.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _words.Count; i++)
            {
                string source = ExportPresetManager.FormatWordToSourceLine(_words[i]);
                string result = ExportPresetManager.FormatLine(source, pattern, replacement, i + 1);
                sb.AppendLine(result);
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            string text = FullPreviewTextBox?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                text = GenerateExportText(PresetPatternInput?.Text ?? string.Empty, PresetReplacementInput?.Text ?? string.Empty);
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);

            SoundService.Instance.PlayCopySound();

            var loc = LocalizationService.Instance;
            CopyAllButtonText.Text = loc.GetString("ButtonCopied", "已复制！");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                CopyAllButtonText.Text = loc.GetString("ButtonCopyToClipboard", "复制到剪贴板");
            };
            timer.Start();
        }

        private async void ExportFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = FullPreviewTextBox?.Text ?? string.Empty;
                if (string.IsNullOrEmpty(text))
                {
                    text = GenerateExportText(PresetPatternInput?.Text ?? string.Empty, PresetReplacementInput?.Text ?? string.Empty);
                }

                var savePicker = new FileSavePicker();
                if (App.MainWindowInstance != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                }

                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("Text File (*.txt)", new List<string> { ".txt" });
                savePicker.FileTypeChoices.Add("Tab-Separated Values (*.tsv)", new List<string> { ".tsv" });
                savePicker.FileTypeChoices.Add("All Files (*.*)", new List<string> { "." });
                savePicker.SuggestedFileName = $"VocabExport_{DateTime.Now:yyyyMMdd_HHmmss}";

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, text);
                    var loc = LocalizationService.Instance;
                    ExportFileButtonText.Text = loc.GetString("ButtonExportSuccess", "已保存！");

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        ExportFileButtonText.Text = loc.GetString("ButtonExportToFile", "导出为文件...");
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "FormatExportDialog.ExportFileButton_Click");
            }
        }
    }
}
