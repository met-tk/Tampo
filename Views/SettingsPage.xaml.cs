using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage()
        {
            this.InitializeComponent();
            ViewModel = App.GetService<SettingsViewModel>();
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

            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavSettings", "设置");
            if (SectionAppearanceTextBlock != null) SectionAppearanceTextBlock.Text = loc.GetString("AppearanceAndLanguage", "外观与语言");
            if (ThemeLabelTextBlock != null) ThemeLabelTextBlock.Text = loc.GetString("ThemeSettings", "深浅主题");
            if (LanguageLabelTextBlock != null) LanguageLabelTextBlock.Text = loc.GetString("Language", "语言");

            if (ThemeComboBox != null)
            {
                int curThemeIdx = ThemeComboBox.SelectedIndex;
                foreach (ComboBoxItem item in ThemeComboBox.Items)
                {
                    if (item.Tag is string tag)
                    {
                        item.Content = tag switch
                        {
                            "Default" => loc.GetString("ThemeDefault", "跟随系统"),
                            "Light" => loc.GetString("ThemeLight", "浅色模式"),
                            "Dark" => loc.GetString("ThemeDark", "深色模式"),
                            _ => item.Content
                        };
                    }
                }
                if (curThemeIdx >= 0)
                {
                    ThemeComboBox.SelectedIndex = -1;
                    ThemeComboBox.SelectedIndex = curThemeIdx;
                }
            }

            if (SectionLogsTextBlock != null) SectionLogsTextBlock.Text = loc.GetString("SectionLogs", "运行日志");
            if (LogPathLabelTextBlock != null) LogPathLabelTextBlock.Text = loc.GetString("LogPathLabel", "日志存储路径");
            if (OpenLogButton != null) OpenLogButton.Content = loc.GetString("ButtonOpenDirectory", "打开日志目录");
            if (LogPathTextBlock != null) LogPathTextBlock.Text = CrashLogger.GetLogPath();

            if (SectionDataTextBlock != null) SectionDataTextBlock.Text = loc.GetString("DataMaintenance", "数据维护");
            if (ClearDataTitleTextBlock != null) ClearDataTitleTextBlock.Text = loc.GetString("ClearDataLabel", "清空所有词库与学习数据");
            if (ClearDataSubTextBlock != null) ClearDataSubTextBlock.Text = loc.GetString("ClearDataSubLabel", "重置本地 SQLite 数据库，将移除所有已导入词汇与复习进度。");
            if (ClearDataButtonText != null) ClearDataButtonText.Text = loc.GetString("ButtonClearAllData", "清空并重置");

            if (SectionAboutTextBlock != null) SectionAboutTextBlock.Text = loc.GetString("AboutTitle", "关于 Tampo");
            if (AboutVersionTextBlock != null) AboutVersionTextBlock.Text = loc.GetString("VersionLabel", "版本: v1.1.0 (Windows App SDK / WinUI 3)");
            if (AboutArchTextBlock != null) AboutArchTextBlock.Text = loc.GetString("ArchLabel", "架构: x64 Self-Contained 独立部署");
            if (AboutEngineTextBlock != null) AboutEngineTextBlock.Text = loc.GetString("EngineLabel", "算法引擎: FSRS v4.5 (Free Spaced Repetition Scheduler)");
            if (AuthorLinkButton != null) AuthorLinkButton.Content = loc.GetString("AuthorLinkText", "找我玩");
            if (AboutContactLabel != null) AboutContactLabel.Text = loc.GetString("AboutContactLabel", "问题反馈与联系:");

            // 音效与提示设置本地化
            if (SectionSoundTextBlock != null) SectionSoundTextBlock.Text = loc.GetString("SectionSound", "音效与提示");
            if (SoundToggleSubTextBlock != null) SoundToggleSubTextBlock.Text = loc.GetString("SoundToggleSub", "开启打卡标记与中键复制时的音频反馈提示");
            if (StudyMarkSoundLabelTextBlock != null) StudyMarkSoundLabelTextBlock.Text = loc.GetString("StudyMarkSoundLabel", "【学习】单词打卡标记音效");
            if (CopySoundLabelTextBlock != null) CopySoundLabelTextBlock.Text = loc.GetString("CopySoundLabel", "鼠标中键复制单词音效");
            if (TestStudyMarkSoundText != null) TestStudyMarkSoundText.Text = loc.GetString("ButtonTestSound", "试听");
            if (TestCopySoundText != null) TestCopySoundText.Text = loc.GetString("ButtonTestSound", "试听");
            if (ImportStudyMarkSoundText != null) ImportStudyMarkSoundText.Text = loc.GetString("ButtonImportAudio", "导入音频...");
            if (ImportCopySoundText != null) ImportCopySoundText.Text = loc.GetString("ButtonImportAudio", "导入音频...");
            if (ResetStudyMarkSoundText != null) ResetStudyMarkSoundText.Text = loc.GetString("ButtonResetDefault", "恢复默认");
            if (ResetCopySoundText != null) ResetCopySoundText.Text = loc.GetString("ButtonResetDefault", "恢复默认");

            UpdateSoundUI();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 初始化 ComboBox 选中状态
            string currentLang = LocalizationService.Instance.CurrentLanguage;
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            ThemeComboBox.SelectedIndex = ViewModel.CurrentTheme switch
            {
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string themeStr)
            {
                ElementTheme theme = themeStr switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
                ViewModel.SetTheme(theme);
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string langTag)
            {
                if (langTag != LocalizationService.Instance.CurrentLanguage)
                {
                    ViewModel.SetLanguage(langTag);
                }
            }
        }

        private void OpenLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logFile = CrashLogger.GetLogPath();
                string? dir = Path.GetDirectoryName(logFile);
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.OpenLogDirectory_Click");
            }
        }

        private async void ClearAllData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var loc = LocalizationService.Instance;
                var confirmDialog = new ContentDialog
                {
                    Title = loc.GetString("ClearDataDialogTitle", "警告：确认清除所有导入记录？"),
                    Content = DialogHelper.CreateTextBlockContent(loc.GetString("ClearDataDialogContent", "此操作将永久清空本地数据库中的所有已导入单词、全部自定义词单以及历史 FSRS 复习打卡日志。数据将无法恢复。\n\n是否确认继续清空？")),
                    PrimaryButtonText = loc.GetString("ButtonConfirmClear", "确认清空"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };

                var res = await confirmDialog.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await ViewModel.ClearAllDataAsync();

                    var tip = new ContentDialog
                    {
                        Title = loc.GetString("ButtonConfirm", "确定"),
                        Content = DialogHelper.CreateTextBlockContent(loc.GetString("MsgClearDataSuccess", "所有单词、词单及复习数据已完全清空。")),
                        CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                        XamlRoot = this.XamlRoot
                    };
                    await tip.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.ClearAllData_Click");
            }
        }

        #region 提示音设置交互逻辑
        private void UpdateSoundUI()
        {
            var loc = LocalizationService.Instance;
            var sound = SoundService.Instance;

            if (SoundToggleSwitch != null)
            {
                SoundToggleSwitch.IsOn = sound.IsEnabled;
            }

            if (StudyMarkSoundDescTextBlock != null)
            {
                if (string.IsNullOrWhiteSpace(sound.StudyMarkSoundPath) || !File.Exists(sound.StudyMarkSoundPath))
                {
                    StudyMarkSoundDescTextBlock.Text = loc.GetString("SoundDefaultStudyDesc", "当前音效：Windows 默认通知音效");
                }
                else
                {
                    StudyMarkSoundDescTextBlock.Text = string.Format(loc.GetString("SoundCustomDescFormat", "当前自定义音频：{0}"), Path.GetFileName(sound.StudyMarkSoundPath));
                }
            }

            if (CopySoundDescTextBlock != null)
            {
                if (string.IsNullOrWhiteSpace(sound.CopySoundPath) || !File.Exists(sound.CopySoundPath))
                {
                    CopySoundDescTextBlock.Text = loc.GetString("SoundDefaultCopyDesc", "当前音效：Windows 默认提示音效");
                }
                else
                {
                    CopySoundDescTextBlock.Text = string.Format(loc.GetString("SoundCustomDescFormat", "当前自定义音频：{0}"), Path.GetFileName(sound.CopySoundPath));
                }
            }
        }

        private void SoundToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (SoundToggleSwitch != null)
            {
                SoundService.Instance.IsEnabled = SoundToggleSwitch.IsOn;
            }
        }

        private void TestStudyMarkSound_Click(object sender, RoutedEventArgs e)
        {
            SoundService.Instance.TestPlay(SoundService.Instance.StudyMarkSoundPath, true);
        }

        private async void ImportStudyMarkSound_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".wma");
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;

                var file = await picker.PickSingleFileAsync();
                if (file != null && !string.IsNullOrWhiteSpace(file.Path))
                {
                    SoundService.Instance.StudyMarkSoundPath = file.Path;
                    UpdateSoundUI();
                    SoundService.Instance.TestPlay(file.Path, true);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.ImportStudyMarkSound_Click");
            }
        }

        private void ResetStudyMarkSound_Click(object sender, RoutedEventArgs e)
        {
            SoundService.Instance.StudyMarkSoundPath = string.Empty;
            UpdateSoundUI();
        }

        private void TestCopySound_Click(object sender, RoutedEventArgs e)
        {
            SoundService.Instance.TestPlay(SoundService.Instance.CopySoundPath, false);
        }

        private async void ImportCopySound_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add(".wav");
                picker.FileTypeFilter.Add(".mp3");
                picker.FileTypeFilter.Add(".m4a");
                picker.FileTypeFilter.Add(".wma");
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;

                var file = await picker.PickSingleFileAsync();
                if (file != null && !string.IsNullOrWhiteSpace(file.Path))
                {
                    SoundService.Instance.CopySoundPath = file.Path;
                    UpdateSoundUI();
                    SoundService.Instance.TestPlay(file.Path, false);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.ImportCopySound_Click");
            }
        }

        private void ResetCopySound_Click(object sender, RoutedEventArgs e)
        {
            SoundService.Instance.CopySoundPath = string.Empty;
            UpdateSoundUI();
        }

        private async void AboutContactLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = new Uri("mailto:yunotaketo@gmail.com?subject=Tampo%20Feedback");
                bool launched = await Windows.System.Launcher.LaunchUriAsync(uri);
                if (!launched)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "mailto:yunotaketo@gmail.com",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "mailto:yunotaketo@gmail.com",
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SettingsPage.AboutContactLinkButton_Click");
                }
            }
        }
        #endregion
    }
}
