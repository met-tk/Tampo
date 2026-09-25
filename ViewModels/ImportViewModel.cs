using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NihongoVocab.Models;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class ImportViewModel : ObservableObject
    {
        private readonly DatabaseService _databaseService;
        private readonly PresetManager _presetManager;
        private CancellationTokenSource? _throttleCts;

        [ObservableProperty]
        private string _rawText = string.Empty;

        [ObservableProperty]
        private CleansingPreset? _selectedPreset;

        [ObservableProperty]
        private int _extractedCount = 0;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ObservableCollection<CleansingPreset> Presets => _presetManager.Presets;
        public ObservableCollection<string> CleanedWords { get; } = new();

        public string ImportTitle => LocalizationService.Instance.GetString("ImportTitle", "批量导入与正则清洗");
        public string PresetSelectorLabel => LocalizationService.Instance.GetString("PresetSelectorLabel", "清洗预设：");
        public string ManagePresets => LocalizationService.Instance.GetString("ManagePresets", "管理预设...");
        public string ButtonImportToVault => LocalizationService.Instance.GetString("ButtonImportToVault", "导入到词库");
        public string ImportRawTextTip => LocalizationService.Instance.GetString("ImportRawTextTip", "原始文本（悬浮右侧候选词可双向溯源高亮定位）");
        public string OriginalTextPlaceholder => LocalizationService.Instance.GetString("OriginalTextPlaceholder", "在此粘贴原始日语文本、电子书笔记或段落...");
        public string ImportPreviewTip => LocalizationService.Instance.GetString("ImportPreviewTip", "候选词清洗预览（悬浮高亮原文，点击右上角删除单项）");
        public string ExtractedCountLabel => LocalizationService.Instance.GetString("ExtractedCountLabel", "已提取：");
        public string RemoveCandidateTip => LocalizationService.Instance.GetString("RemoveCandidateTip", "移除此候选词");

        public ImportViewModel(DatabaseService databaseService, PresetManager presetManager)
        {
            _databaseService = databaseService;
            _presetManager = presetManager;

            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                OnPropertyChanged(string.Empty);
            };

            InitPresetSelection();
        }

        private async void InitPresetSelection()
        {
            try
            {
                var prefPreset = await _databaseService.GetPreferenceAsync("Import_SelectedPresetName", "");
                if (!string.IsNullOrEmpty(prefPreset))
                {
                    var found = Presets.FirstOrDefault(p => p.Name == prefPreset);
                    if (found != null)
                    {
                        SelectedPreset = found;
                        return;
                    }
                }

                if (Presets.Count > 0 && SelectedPreset == null)
                {
                    SelectedPreset = Presets[0];
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ImportViewModel.InitPresetSelection");
            }
        }

        partial void OnRawTextChanged(string value)
        {
            TriggerThrottleCleansing();
        }

        partial void OnSelectedPresetChanged(CleansingPreset? value)
        {
            if (value != null && !string.IsNullOrEmpty(value.Name))
            {
                _ = _databaseService.SetPreferenceAsync("Import_SelectedPresetName", value.Name);
            }
            TriggerThrottleCleansing();
        }

        /// <summary>
        /// 150ms 节流触发正则匹配管道
        /// </summary>
        private void TriggerThrottleCleansing()
        {
            _throttleCts?.Cancel();
            _throttleCts = new CancellationTokenSource();
            var token = _throttleCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150, token);
                    if (token.IsCancellationRequested) return;

                    var preset = SelectedPreset;
                    var text = RawText;
                    if (preset == null || string.IsNullOrWhiteSpace(text))
                    {
                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            CleanedWords.Clear();
                            ExtractedCount = 0;
                        });
                        return;
                    }

                    var results = _presetManager.Cleanse(text, preset);

                    App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        CleanedWords.Clear();
                        foreach (var item in results)
                        {
                            CleanedWords.Add(item);
                        }
                        ExtractedCount = CleanedWords.Count;
                    });
                }
                catch (OperationCanceledException)
                {
                    // 节流取消，正常
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "ImportViewModel.TriggerThrottleCleansing");
                }
            }, token);
        }

        public void RemoveCandidateWord(string word)
        {
            if (CleanedWords.Remove(word))
            {
                ExtractedCount = CleanedWords.Count;
            }
        }

        [ObservableProperty]
        private ImportResult? _lastImportResult;

        [RelayCommand]
        public async Task<ImportResult?> ImportWordsAsync()
        {
            if (CleanedWords.Count == 0) return null;

            try
            {
                var result = await _databaseService.AddWordsWithResultAsync(CleanedWords);
                LastImportResult = result;
                StatusMessage = $"成功导入 {result.InsertedCount} 个新词，自动去重跳过 {result.DuplicatedCount} 个已存在单词！";
                RawText = string.Empty;
                CleanedWords.Clear();
                ExtractedCount = 0;
                return result;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ImportViewModel.ImportWordsAsync");
                StatusMessage = $"导入失败：{ex.Message}";
                throw;
            }
        }

        public void ReloadPresets()
        {
            _presetManager.LoadPresets();
        }
    }
}
