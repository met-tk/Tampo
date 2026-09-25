using System;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.Views.Dialogs
{
    public sealed partial class ManagePresetsDialog : ContentDialog
    {
        private readonly PresetManager _presetManager;

        public ManagePresetsDialog(PresetManager presetManager)
        {
            _presetManager = presetManager;
            this.Resources["ContentDialogMinWidth"] = 780.0;
            this.Resources["ContentDialogMaxWidth"] = 920.0;
            this.InitializeComponent();
            var loc = LocalizationService.Instance;
            this.Title = loc.GetString("PresetDialogTitle", "正则清洗预设管理");
            this.PrimaryButtonText = loc.GetString("ButtonFinish", "完成");
            this.CloseButtonText = loc.GetString("ButtonClose", "关闭");

            if (SavedRulesTitleText != null) SavedRulesTitleText.Text = loc.GetString("SavedRulesTitle", "已保存规则库");
            if (SavedRulesTipText != null) SavedRulesTipText.Text = loc.GetString("SavedRulesTip", "点击选择可带入右侧表单进行测试或编辑");
            if (ConfigRulesTitleText != null) ConfigRulesTitleText.Text = loc.GetString("ConfigRulesTitle", "自定义清洗规则配置");
            if (PresetNameInput != null)
            {
                PresetNameInput.Header = loc.GetString("PresetNameHeader", "预设名称");
                PresetNameInput.PlaceholderText = loc.GetString("PresetNamePlaceholder", "输入预设名称...");
            }
            if (CaptureGroupInput != null) CaptureGroupInput.Header = loc.GetString("CaptureGroupHeader", "捕获组索引");
            if (PresetPatternInput != null)
            {
                PresetPatternInput.Header = loc.GetString("PresetPatternHeader", "正则表达式");
                PresetPatternInput.PlaceholderText = loc.GetString("PresetPatternPlaceholder", "正则表达式（如：([ぁ-ん]+)）...");
            }
            if (AddPresetButtonText != null) AddPresetButtonText.Text = loc.GetString("ButtonSaveNewPreset", "保存为新预设");
            if (InstantVerifyTitleText != null) InstantVerifyTitleText.Text = loc.GetString("InstantVerifyTitle", "单行即时提取验证");
            if (TestLineInput != null) TestLineInput.PlaceholderText = loc.GetString("TestLinePlaceholder", "输入单行测试文本...");
            if (IsInverseFilterCheckBox != null)
            {
                IsInverseFilterCheckBox.Content = loc.GetString("IsInverseFilterLabel", "反向过滤");
                ToolTipService.SetToolTip(IsInverseFilterCheckBox, loc.GetString("IsInverseFilterToolTip", "勾选后为反向模式，匹配字符将被剔除或删除；未勾选时为正向提取模式"));
            }
            if (MatchWholeLineCheckBox != null)
            {
                MatchWholeLineCheckBox.Content = loc.GetString("MatchWholeLineLabel", "匹配整行");
                ToolTipService.SetToolTip(MatchWholeLineCheckBox, loc.GetString("MatchWholeLineToolTip", "勾选后无论正向还是反向均对整行生效：反向时只要行内命中任意字符即删除整行；正向时只要行内命中任意字符即整行输出"));
            }
            if (CaptureGroupInput != null)
            {
                CaptureGroupInput.ValueChanged += (s, e) => UpdateValidationPreview();
            }

            RefreshPresets();
            UpdateValidationPreview();
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
            if (PresetsListView.SelectedItem is CleansingPreset preset)
            {
                PresetNameInput.Text = preset.Name;
                PresetPatternInput.Text = preset.Pattern;
                CaptureGroupInput.Value = preset.CaptureGroupIndex;
                if (IsInverseFilterCheckBox != null)
                {
                    IsInverseFilterCheckBox.IsChecked = preset.IsInverseFilter;
                }
                if (MatchWholeLineCheckBox != null)
                {
                    MatchWholeLineCheckBox.IsChecked = preset.MatchWholeLine;
                }
                UpdateValidationPreview();
            }
        }

        private void IsInverseFilterCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateValidationPreview();
        }

        private void MatchWholeLineCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateValidationPreview();
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            string name = PresetNameInput.Text?.Trim() ?? string.Empty;
            string pattern = PresetPatternInput.Text?.Trim() ?? string.Empty;
            int group = (int)CaptureGroupInput.Value;
            bool isInverse = IsInverseFilterCheckBox?.IsChecked ?? false;
            bool matchWholeLine = MatchWholeLineCheckBox?.IsChecked ?? false;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern))
            {
                return;
            }

            var newPreset = new CleansingPreset
            {
                Name = name,
                Pattern = pattern,
                CaptureGroupIndex = group,
                IsInverseFilter = isInverse,
                MatchWholeLine = matchWholeLine,
                IsBuiltIn = false,
                Description = isInverse
                    ? (matchWholeLine ? "自定义预设 (反向删除整行)" : "自定义预设 (反向剔除字符)")
                    : (matchWholeLine ? "自定义预设 (正向整行匹配)" : "自定义预设 (正向片段提取)")
            };

            _presetManager.AddPreset(newPreset);
            RefreshPresets();
            PresetsListView.SelectedItem = newPreset;
        }

        private void ConfirmDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CleansingPreset preset)
            {
                _presetManager.RemovePreset(preset);
                RefreshPresets();
            }
        }

        private void PresetPatternInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateValidationPreview();
        }

        private void TestLineInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateValidationPreview();
        }

        private void UpdateValidationPreview()
        {
            if (ValidationResultTextBlock == null) return;
            var loc = LocalizationService.Instance;

            string pattern = PresetPatternInput?.Text ?? string.Empty;
            string testLine = TestLineInput?.Text ?? string.Empty;
            int group = (int)(CaptureGroupInput?.Value ?? 1);
            bool isInverse = IsInverseFilterCheckBox?.IsChecked ?? false;
            bool matchWholeLine = MatchWholeLineCheckBox?.IsChecked ?? false;

            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(testLine))
            {
                ValidationResultTextBlock.Text = loc.GetString("ValidationNoInput", "提取结果：(无输入)");
                return;
            }

            try
            {
                var regex = new Regex(pattern, RegexOptions.Multiline);
                var match = regex.Match(testLine);

                if (isInverse)
                {
                    if (matchWholeLine)
                    {
                        if (match.Success)
                        {
                            ValidationResultTextBlock.Text = loc.GetString("ValidationInverseLineExcluded", "命中反向规则：整行将被删除剔除");
                        }
                        else
                        {
                            ValidationResultTextBlock.Text = string.Format(loc.GetString("ValidationRetainedFormat", "未命中排除规则：保留文本「{0}」"), testLine.Trim());
                        }
                    }
                    else
                    {
                        var matches = regex.Matches(testLine);
                        string cleaned = regex.Replace(testLine, string.Empty).Trim();
                        if (matches.Count > 0)
                        {
                            ValidationResultTextBlock.Text = string.Format(loc.GetString("ValidationMatchOnlyExcludedFormat", "已剔除 {0} 处命中内容，保留剩余文本：「{1}」"), matches.Count, cleaned);
                        }
                        else
                        {
                            ValidationResultTextBlock.Text = string.Format(loc.GetString("ValidationNoMatchRetainedFormat", "无命中内容，完整保留文本：「{0}」"), testLine.Trim());
                        }
                    }
                }
                else
                {
                    if (matchWholeLine)
                    {
                        if (match.Success)
                        {
                            ValidationResultTextBlock.Text = string.Format(loc.GetString("ValidationNormalLineMatchedFormat", "命中规则：整行输出「{0}」"), testLine.Trim());
                        }
                        else
                        {
                            ValidationResultTextBlock.Text = loc.GetString("ValidationNoMatch", "未匹配到任何结果。");
                        }
                    }
                    else
                    {
                        if (match.Success)
                        {
                            string extracted = group > 0 && match.Groups.Count > group
                                ? match.Groups[group].Value
                                : match.Value;

                            ValidationResultTextBlock.Text = string.Format(loc.GetString("ValidationSuccessFormat", "提取成功：「{0}」 (捕获组: {1})"), extracted.Trim(), group);
                        }
                        else
                        {
                            ValidationResultTextBlock.Text = loc.GetString("ValidationNoMatch", "未匹配到任何结果。");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ValidationResultTextBlock.Text = $"{loc.GetString("ValidationRegexError", "正则语法错误：")}{ex.Message}";
            }
        }
    }
}
