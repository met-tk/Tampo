using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
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

            this.Loaded += async (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
                UpdateLocalizedStrings();

                DatabaseService.DataChanged -= OnDatabaseDataChanged;
                DatabaseService.DataChanged += OnDatabaseDataChanged;
                SyncServiceController.Instance.ServiceStatusChanged -= OnSyncServiceStatusChanged;
                SyncServiceController.Instance.ServiceStatusChanged += OnSyncServiceStatusChanged;

                await UpdateSyncServiceUIAsync();
                await LoadPairedDevicesAsync();
                await RefreshBackupsListAsync();
            };
            this.Unloaded += (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                DatabaseService.DataChanged -= OnDatabaseDataChanged;
                SyncServiceController.Instance.ServiceStatusChanged -= OnSyncServiceStatusChanged;
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

            if (PageTitleTextBlock != null) PageTitleTextBlock.Text = loc.GetString("NavSettings", "设置");
            if (SectionAppearanceTextBlock != null) SectionAppearanceTextBlock.Text = loc.GetString("AppearanceAndLanguage", "外观与语言");
            if (ThemeLabelTextBlock != null) ThemeLabelTextBlock.Text = loc.GetString("ThemeSettings", "深浅主题");
            if (LanguageLabelTextBlock != null) LanguageLabelTextBlock.Text = loc.GetString("Language", "语言");
            if (DayRolloverLabelTextBlock != null) DayRolloverLabelTextBlock.Text = loc.GetString("DayRolloverLabel", "每日结算时刻");
            if (DayRolloverSubTextBlock != null) DayRolloverSubTextBlock.Text = loc.GetString("DayRolloverSubLabel", "跨零点熬夜学习时，在此时间前仍计入前一天（默认 04:00）");

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

            if (SectionDataTextBlock != null) SectionDataTextBlock.Text = loc.GetString("DataMaintenance", "数据安全与灾备恢复");
            if (DataSafetyDescTextBlock != null) DataSafetyDescTextBlock.Text = loc.GetString("DataSafetyDesc", "多重灾备保护：支持从手机主动拉取全量词库、本地安全快照秒级还原与外部备份管理");
            if (PullFromMobileTitleTextBlock != null) PullFromMobileTitleTextBlock.Text = loc.GetString("PullFromMobileTitle", "从手机端主动拉取全量数据");
            if (PullFromMobileDescTextBlock != null) PullFromMobileDescTextBlock.Text = loc.GetString("PullFromMobileDesc", "电脑端数据异常或丢失时，向已连接手机发送指令，将手机完整词库与进度推回电脑并安全合并");
            if (PullFromMobileButtonText != null) PullFromMobileButtonText.Text = loc.GetString("ButtonPullFromMobile", "从手机主动拉取");
            if (SyncOnStudyLabelTextBlock != null) SyncOnStudyLabelTextBlock.Text = loc.GetString("SyncOnStudyLabel", "本地打卡后自动同步");
            if (SyncOnStudyDescTextBlock != null) SyncOnStudyDescTextBlock.Text = loc.GetString("SyncOnStudyDesc", "在电脑端完成单词打卡后，自动向局域网已连接的移动端广播数据变动；关闭后仅本地记录，不向手机广播");
            if (LocalBackupsTitleTextBlock != null) LocalBackupsTitleTextBlock.Text = loc.GetString("LocalBackupsTitle", "本地自动快照一键还原");
            if (LocalBackupsDescTextBlock != null) LocalBackupsDescTextBlock.Text = loc.GetString("LocalBackupsDesc", "系统在同步与数据变更前自动保存快照。在下方列表中可对任意历史快照一键安全还原或快速删除");
            if (NoSnapshotsTextBlock != null) NoSnapshotsTextBlock.Text = loc.GetString("NoSnapshotsText", "暂无可用备份快照。点击上方【创建快照】可立即生成本地备份。");
            if (CreateSnapshotButtonText != null) CreateSnapshotButtonText.Text = loc.GetString("ButtonCreateBackup", "创建快照");
            if (RestoreFromFileTitleTextBlock != null) RestoreFromFileTitleTextBlock.Text = loc.GetString("RestoreFromFileTitle", "从外部备份文件还原");
            if (RestoreFromFileDescTextBlock != null) RestoreFromFileDescTextBlock.Text = loc.GetString("RestoreFromFileDesc", "选择自定义位置的 Tampo SQLite 数据库备份文件 (.db) 恢复本地数据");
            if (BrowseAndRestoreButtonText != null) BrowseAndRestoreButtonText.Text = loc.GetString("ButtonRestoreFromFile", "浏览文件并还原...");
            if (ClearDataTitleTextBlock != null) ClearDataTitleTextBlock.Text = loc.GetString("ClearDataLabel", "清空所有词库与学习数据");
            if (ClearDataSubTextBlock != null) ClearDataSubTextBlock.Text = loc.GetString("ClearDataSubLabel", "重置本地 SQLite 数据库，将移除所有已导入词汇与复习进度。");
            if (ClearDataButtonText != null) ClearDataButtonText.Text = loc.GetString("ButtonClearAllData", "清空并重置");

            if (SectionAboutTextBlock != null) SectionAboutTextBlock.Text = loc.GetString("AboutTitle", "关于 Tampo");
            if (AboutVersionTextBlock != null) AboutVersionTextBlock.Text = loc.GetString("VersionLabel", $"版本: v{LocalizationService.AppVersion} (Windows App SDK / WinUI 3)");
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

            // 局域网同步守护服务本地化
            if (LanSyncSectionTextBlock != null) LanSyncSectionTextBlock.Text = loc.GetString("LanSyncSectionTitle", "局域网同步守护服务");
            if (LanSyncDescTextBlock != null) LanSyncDescTextBlock.Text = loc.GetString("LanSyncDesc", "后台静默常驻运行，支持移动端在局域网内自动互联与无感双向合并同步");
            if (SyncAutostartLabelTextBlock != null) SyncAutostartLabelTextBlock.Text = loc.GetString("SyncAutostartLabel", "开机自启动守护服务");
            if (SyncAutostartDescTextBlock != null) SyncAutostartDescTextBlock.Text = loc.GetString("SyncAutostartDesc", "Windows 开机后自动在后台静默运行同步服务，手机端可随时即刻同步，无需先手动打开电脑主程序");
            if (PairingCodeSectionTitle != null) PairingCodeSectionTitle.Text = loc.GetString("PairingSectionTitle", "绑定新移动设备");
            if (PairingCodeDescTextBlock != null) PairingCodeDescTextBlock.Text = loc.GetString("PairingSectionDesc", "在手机端输入此 6 位配对码即可建立长效免密绑定，后续无需再手动配置 IP 或配对码");
            if (GeneratePairingCodeButtonText != null) GeneratePairingCodeButtonText.Text = loc.GetString("ButtonGeneratePairingCode", "生成 6 位配对码");
            if (PairedDevicesTitleTextBlock != null) PairedDevicesTitleTextBlock.Text = loc.GetString("PairedDevicesTitle", "已绑定设备管理");
            if (PairedDevicesDescTextBlock != null) PairedDevicesDescTextBlock.Text = loc.GetString("PairedDevicesDesc", "已获得长期同步授权的移动设备。解除绑定后，该设备将失去同步权限并需重新配对");
            if (RefreshDevicesButtonText != null) RefreshDevicesButtonText.Text = loc.GetString("ButtonRefreshList", "刷新列表");
            if (NoDevicesTextBlock != null) NoDevicesTextBlock.Text = loc.GetString("NoDevicesText", "暂无已绑定设备。请点击上方【生成 6 位配对码】并在手机端输入完成首次绑定。");
            if (CopyPairingCodeText != null) CopyPairingCodeText.Text = loc.GetString("ButtonCopy", "复制");

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

            int currentRollover = Math.Clamp(UserPreferenceService.Instance.GetInt("App_DayRolloverHour", FsrsEngine.DayRolloverHour), 0, 6);
            if (DayRolloverComboBox != null && currentRollover >= 0 && currentRollover < DayRolloverComboBox.Items.Count)
            {
                DayRolloverComboBox.SelectedIndex = currentRollover;
            }
        }

        private void DayRolloverComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DayRolloverComboBox.SelectedItem is ComboBoxItem item && item.Tag is string hourStr && int.TryParse(hourStr, out int hour))
            {
                if (hour != FsrsEngine.DayRolloverHour)
                {
                    ViewModel.SetDayRolloverHour(hour);
                }
            }
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
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };

                MainWindow.RegisterActiveDialog(confirmDialog);
                var res = await confirmDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(confirmDialog);

                if (res == ContentDialogResult.Primary)
                {
                    await SyncServiceController.Instance.SetMaintenanceModeAsync(true);
                    bool wasRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
                    if (wasRunning)
                    {
                        await SyncServiceController.Instance.StopServiceAsync();
                    }

                    try
                    {
                        await ViewModel.ClearAllDataAsync();
                        await RefreshBackupsListAsync();

                        var tip = new ContentDialog
                        {
                            Title = loc.GetString("ButtonConfirm", "确定"),
                            Content = DialogHelper.CreateTextBlockContent(loc.GetString("MsgClearDataSuccess", "所有单词、词单及复习数据已完全清空，同步纪元已重置。")),
                            CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                        };
                        MainWindow.RegisterActiveDialog(tip);
                        await tip.ShowAsync();
                        MainWindow.UnregisterActiveDialog(tip);
                    }
                    finally
                    {
                        if (wasRunning)
                        {
                            await SyncServiceController.Instance.StartServiceAsync();
                        }
                        await SyncServiceController.Instance.SetMaintenanceModeAsync(false);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.ClearAllData_Click");
            }
        }

        #region 数据安全与灾备恢复交互逻辑

        private async void PullFromMobile_Click(object sender, RoutedEventArgs e)
        {
            var triggerBtn = sender as Button;
            if (triggerBtn != null) triggerBtn.IsEnabled = false;
            try
            {
                var loc = LocalizationService.Instance;
                bool isRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
                if (!isRunning)
                {
                    var tip = new ContentDialog
                    {
                        Title = loc.GetString("SyncServiceNotRunningTitle", "局域网同步服务未启动"),
                        Content = DialogHelper.CreateTextBlockContent(loc.GetString("SyncServiceNotRunningMsg", "局域网同步服务当前未运行，请先在上方开启【局域网同步守护服务】后再执行拉取。")),
                        CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };
                    MainWindow.RegisterActiveDialog(tip);
                    await tip.ShowAsync();
                    MainWindow.UnregisterActiveDialog(tip);
                    return;
                }

                var devices = await _dbService.GetPairedDevicesAsync();
                if (devices == null || devices.Count == 0)
                {
                    var tip = new ContentDialog
                    {
                        Title = loc.GetString("NoPairedDevicesTitle", "未绑定任何移动设备"),
                        Content = DialogHelper.CreateTextBlockContent(loc.GetString("NoPairedDevicesMsg", "当前尚未绑定任何手机设备。请先点击【生成 6 位配对码】并在手机端完成配对后再执行拉取。")),
                        CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };
                    MainWindow.RegisterActiveDialog(tip);
                    await tip.ShowAsync();
                    MainWindow.UnregisterActiveDialog(tip);
                    return;
                }

                // 获取实时在线状态
                var liveDevices = await SyncServiceController.Instance.GetConnectedDevicesStatusAsync();
                var liveMap = liveDevices.ToDictionary(d => d.Id, d => d);
                foreach (var d in devices)
                {
                    if (liveMap.TryGetValue(d.Id, out var live))
                    {
                        d.IsOnline = live.IsOnline;
                        if (!string.IsNullOrWhiteSpace(live.ClientIp)) d.ClientIp = live.ClientIp;
                        if (!string.IsNullOrWhiteSpace(live.DeviceModel)) d.DeviceModel = live.DeviceModel;
                    }
                }

                string? selectedDeviceId = null;
                string? selectedDeviceName = null;

                if (devices.Count == 1)
                {
                    // 仅绑定 1 部手机：直接定向拉取
                    selectedDeviceId = devices[0].Id;
                    selectedDeviceName = devices[0].DisplayTitle;
                }
                else
                {
                    // 绑定多部手机：弹窗提供清晰选择
                    var sp = new StackPanel { Spacing = 10 };
                    sp.Children.Add(new TextBlock
                    {
                        Text = loc.GetString("SelectPullDeviceDesc", "检测到已绑定多部移动设备，请指定要拉取数据的目标手机："),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13
                    });

                    var radioGroup = new RadioButtons();
                    int defaultIdx = 0;
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        string status = dev.IsOnline ? "🟢 [在线]" : "⚪ [离线]";
                        radioGroup.Items.Add($"{status} {dev.DisplayTitle} ({dev.ClientIp ?? "未知 IP"})");
                        if (dev.IsOnline && defaultIdx == 0) defaultIdx = i;
                    }
                    radioGroup.Items.Add("📢 广播给所有已连接手机");
                    radioGroup.SelectedIndex = defaultIdx;
                    sp.Children.Add(radioGroup);

                    var selectDialog = new ContentDialog
                    {
                        Title = loc.GetString("SelectPullDeviceTitle", "选择拉取源手机设备"),
                        Content = sp,
                        PrimaryButtonText = loc.GetString("ButtonStartPull", "开始拉取"),
                        CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };

                    MainWindow.RegisterActiveDialog(selectDialog);
                    var choiceRes = await selectDialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(selectDialog);

                    if (choiceRes != ContentDialogResult.Primary) return;

                    int chosenIdx = radioGroup.SelectedIndex;
                    if (chosenIdx >= 0 && chosenIdx < devices.Count)
                    {
                        selectedDeviceId = devices[chosenIdx].Id;
                        selectedDeviceName = devices[chosenIdx].DisplayTitle;
                    }
                    else
                    {
                        selectedDeviceId = null;
                        selectedDeviceName = "全部设备";
                    }
                }

                // 1. 拉取前自动创建专属 pre_pull 快照镜像
                await _dbService.CreatePrePullBackupAsync(selectedDeviceName ?? "Mobile");
                await RefreshBackupsListAsync();

                // 2. 发起主动推送指令
                var (success, notified, msg) = await SyncServiceController.Instance.RequestMobilePushAsync(selectedDeviceId);

                string displayMsg = success
                    ? (notified > 0
                        ? string.Format(loc.GetString("PullRequestSentMsg", "已成功向 {0} 台移动设备发送全量推送指令。\n手机端将在数秒内将最新全量词库及复习日志推回电脑并安全合并。"), notified)
                        : (selectedDeviceId != null
                            ? string.Format(loc.GetString("PullTargetOfflineMsg", "目标设备【{0}】当前未与电脑建立实时连接。\n请在手机上打开 Tampo 并保持在同一 WiFi 局域网下，然后重试。"), selectedDeviceName)
                            : loc.GetString("PullNoDeviceMsg", "已广播拉取指令，但当前没有已建立实时长连接的手机端。\n请确保手机端 Tampo 处于打开状态并在同一局域网内，然后重试。")))
                    : string.Format(loc.GetString("PullRequestFailedMsg", "发送拉取指令失败：{0}"), msg);

                var resultDialog = new ContentDialog
                {
                    Title = success ? loc.GetString("PullRequestSuccessTitle", "已发送拉取请求") : loc.GetString("PullRequestFailedTitle", "拉取请求未完成"),
                    Content = DialogHelper.CreateTextBlockContent(displayMsg),
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };
                MainWindow.RegisterActiveDialog(resultDialog);
                await resultDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(resultDialog);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.PullFromMobile_Click");
            }
            finally
            {
                if (triggerBtn != null) triggerBtn.IsEnabled = true;
            }
        }

        private async void RestoreSnapshotItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string backupPath && File.Exists(backupPath))
            {
                await RestoreSpecificSnapshotAsync(backupPath);
            }
        }

        private async Task RestoreSpecificSnapshotAsync(string backupPath)
        {
            try
            {
                var loc = LocalizationService.Instance;
                var confirmDialog = new ContentDialog
                {
                    Title = loc.GetString("RestoreConfirmTitle", "确认还原此数据库快照？"),
                    Content = DialogHelper.CreateTextBlockContent(string.Format(loc.GetString("RestoreConfirmContent", "即将还原备份文件：\n{0}\n\n当前数据库将被此快照覆盖（系统会在还原前自动生成一份 pre_restore 紧急恢复文件，并生成新同步纪元防止手机反向覆写）。\n\n是否确认还原？"), Path.GetFileName(backupPath))),
                    PrimaryButtonText = loc.GetString("ButtonConfirmRestore", "确认还原"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };

                MainWindow.RegisterActiveDialog(confirmDialog);
                var res = await confirmDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(confirmDialog);

                if (res == ContentDialogResult.Primary)
                {
                    await SyncServiceController.Instance.SetMaintenanceModeAsync(true);
                    bool wasRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
                    if (wasRunning)
                    {
                        await SyncServiceController.Instance.StopServiceAsync();
                    }

                    try
                    {
                        bool success = await _dbService.RestoreFromBackupFileAsync(backupPath);
                        if (success)
                        {
                            DateTime fileTime = File.GetCreationTime(backupPath);
                            await _dbService.ApplyMaintenanceRestoredEpochAndPromoteTimestampsAsync(fileTime);
                        }

                        var tip = new ContentDialog
                        {
                            Title = success ? loc.GetString("RestoreSuccessTitle", "快照还原成功") : loc.GetString("RestoreFailedTitle", "还原失败"),
                            Content = DialogHelper.CreateTextBlockContent(success
                                ? loc.GetString("RestoreSuccessMsg", "数据库已成功还原，所有词单、生词与学习进度已刷新，并已建立全新权威纪元锁定防覆盖。")
                                : loc.GetString("RestoreFailedMsg", "还原快照时发生错误，请查看运行日志。")),
                            CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                        };
                        MainWindow.RegisterActiveDialog(tip);
                        await tip.ShowAsync();
                        MainWindow.UnregisterActiveDialog(tip);

                        await RefreshBackupsListAsync();
                    }
                    finally
                    {
                        if (wasRunning)
                        {
                            await SyncServiceController.Instance.StartServiceAsync();
                        }
                        await SyncServiceController.Instance.SetMaintenanceModeAsync(false);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.RestoreSpecificSnapshotAsync");
            }
        }

        private async void CreateSnapshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var backupPath = await _dbService.CreateAutomaticBackupAsync();
                var loc = LocalizationService.Instance;
                var tip = new ContentDialog
                {
                    Title = backupPath != null ? loc.GetString("BackupSuccessTitle", "快照创建成功") : loc.GetString("BackupFailedTitle", "快照创建失败"),
                    Content = DialogHelper.CreateTextBlockContent(backupPath != null
                        ? string.Format(loc.GetString("BackupSuccessMsg", "数据安全快照已成功创建：\n{0}"), Path.GetFileName(backupPath))
                        : loc.GetString("BackupFailedMsg", "创建数据快照失败，请检查磁盘空间或日志。")),
                    CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };
                MainWindow.RegisterActiveDialog(tip);
                await tip.ShowAsync();
                MainWindow.UnregisterActiveDialog(tip);

                await RefreshBackupsListAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.CreateSnapshot_Click");
            }
        }

        private async void BrowseAndRestore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add(".db");
                picker.FileTypeFilter.Add(".sqlite");
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

                var file = await picker.PickSingleFileAsync();
                if (file != null && !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
                {
                    var loc = LocalizationService.Instance;
                    var confirmDialog = new ContentDialog
                    {
                        Title = loc.GetString("RestoreExternalConfirmTitle", "确认从外部数据库文件还原？"),
                        Content = DialogHelper.CreateTextBlockContent(string.Format(loc.GetString("RestoreExternalConfirmMsg", "即将导入外部文件并恢复：\n{0}\n\n当前数据库将被该文件覆盖（系统会在还原前自动生成一份 pre_restore 紧急恢复文件，并生成新同步纪元防止手机反向覆写）。\n\n是否确认继续？"), file.Path)),
                        PrimaryButtonText = loc.GetString("ButtonConfirmRestore", "确认还原"),
                        CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };

                    MainWindow.RegisterActiveDialog(confirmDialog);
                    var res = await confirmDialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(confirmDialog);

                    if (res == ContentDialogResult.Primary)
                    {
                        await SyncServiceController.Instance.SetMaintenanceModeAsync(true);
                        bool wasRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
                        if (wasRunning)
                        {
                            await SyncServiceController.Instance.StopServiceAsync();
                        }

                        try
                        {
                            bool success = await _dbService.RestoreFromBackupFileAsync(file.Path);
                            if (success)
                            {
                                DateTime fileTime = File.GetCreationTime(file.Path);
                                await _dbService.ApplyMaintenanceRestoredEpochAndPromoteTimestampsAsync(fileTime);
                            }

                            var tip = new ContentDialog
                            {
                                Title = success ? loc.GetString("RestoreSuccessTitle", "数据库还原成功") : loc.GetString("RestoreFailedTitle", "还原失败"),
                                Content = DialogHelper.CreateTextBlockContent(success
                                    ? loc.GetString("RestoreSuccessMsg", "外部数据库已成功还原，所有词单、生词与学习进度已刷新，并已建立全新权威纪元锁定防覆盖。")
                                    : loc.GetString("RestoreFailedMsg", "还原外部文件时发生错误，请确认文件是否为有效的 SQLite 数据库。")),
                                CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                                XamlRoot = this.XamlRoot,
                                RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                            };
                            MainWindow.RegisterActiveDialog(tip);
                            await tip.ShowAsync();
                            MainWindow.UnregisterActiveDialog(tip);

                            await RefreshBackupsListAsync();
                        }
                        finally
                        {
                            if (wasRunning)
                            {
                                await SyncServiceController.Instance.StartServiceAsync();
                            }
                            await SyncServiceController.Instance.SetMaintenanceModeAsync(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.BrowseAndRestore_Click");
            }
        }

        private Task RefreshBackupsListAsync()
        {
            try
            {
                var backups = _dbService.GetAvailableBackups();
                var loc = LocalizationService.Instance;

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (BackupSnapshotsListView == null || NoSnapshotsPanel == null) return;

                    if (backups.Count == 0)
                    {
                        BackupSnapshotsListView.ItemsSource = null;
                        BackupSnapshotsListView.Visibility = Visibility.Collapsed;
                        NoSnapshotsPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        var items = new List<BackupSnapshotItem>();
                        foreach (var bPath in backups)
                        {
                            try
                            {
                                var fi = new FileInfo(bPath);
                                string sizeStr = fi.Length >= 1024 * 1024
                                    ? $"{(fi.Length / (1024.0 * 1024.0)):F2} MB"
                                    : $"{(fi.Length / 1024.0):F1} KB";

                                string fn = fi.Name;
                                string badge;
                                if (fn.StartsWith("vocab_auto_"))
                                    badge = loc.GetString("SnapshotTypeAuto", "自动快照");
                                else if (fn.Contains("pre_pull"))
                                    badge = loc.GetString("SnapshotTypePrePull", "拉取前镜像");
                                else if (fn.Contains("pre_restore"))
                                    badge = loc.GetString("SnapshotTypePreRestore", "还原前镜像");
                                else
                                    badge = loc.GetString("SnapshotTypeManual", "手动快照");

                                items.Add(new BackupSnapshotItem
                                {
                                    FilePath = bPath,
                                    FileName = fn,
                                    FormattedTime = fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                                    FormattedSize = sizeStr,
                                    BadgeText = badge,
                                    CreationTime = fi.CreationTime
                                });
                            }
                            catch { }
                        }

                        BackupSnapshotsListView.ItemsSource = items;
                        BackupSnapshotsListView.Visibility = Visibility.Visible;
                        NoSnapshotsPanel.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.RefreshBackupsListAsync");
            }
            return Task.CompletedTask;
        }

        private async void DeleteSnapshotItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string backupPath && File.Exists(backupPath))
            {
                await DeleteSpecificSnapshotAsync(backupPath);
            }
        }

        private async Task DeleteSpecificSnapshotAsync(string backupPath)
        {
            var loc = LocalizationService.Instance;
            try
            {
                string fileName = Path.GetFileName(backupPath);
                var confirmDialog = new ContentDialog
                {
                    Title = loc.GetString("DeleteSnapshotConfirmTitle", "删除备份快照"),
                    Content = DialogHelper.CreateTextBlockContent(string.Format(
                        loc.GetString("DeleteSnapshotConfirmMsg", "确定要物理删除该备份快照文件吗？\n{0}\n\n此操作不可撤销。"),
                        fileName)),
                    PrimaryButtonText = loc.GetString("ButtonDelete", "删除"),
                    CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                };

                MainWindow.RegisterActiveDialog(confirmDialog);
                var result = await confirmDialog.ShowAsync();
                MainWindow.UnregisterActiveDialog(confirmDialog);

                if (result == ContentDialogResult.Primary)
                {
                    bool deleted = _dbService.DeleteBackupFile(backupPath);
                    await RefreshBackupsListAsync();

                    if (!deleted)
                    {
                        var failDialog = new ContentDialog
                        {
                            Title = loc.GetString("DeleteSnapshotFailedTitle", "删除快照失败"),
                            Content = DialogHelper.CreateTextBlockContent(string.Format(
                                loc.GetString("DeleteSnapshotFailedMsg", "未能成功删除快照文件：{0}"),
                                fileName)),
                            CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                        };
                        MainWindow.RegisterActiveDialog(failDialog);
                        await failDialog.ShowAsync();
                        MainWindow.UnregisterActiveDialog(failDialog);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.DeleteSpecificSnapshotAsync");
            }
        }

        private void OpenBackupsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = _dbService.GetBackupsDirectoryPath();
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.OpenBackupsFolder_Click");
            }
        }

        #endregion

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

        #region 局域网同步守护服务交互

        private readonly DatabaseService _dbService = App.GetService<DatabaseService>();

        private void OnDatabaseDataChanged()
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                await LoadPairedDevicesAsync();
                await RefreshBackupsListAsync();
            });
        }

        private void OnSyncServiceStatusChanged(bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateSyncStatusUI(isRunning);
            });
        }

        private async Task UpdateSyncServiceUIAsync()
        {
            bool isRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
            UpdateSyncStatusUI(isRunning);
            if (SyncAutostartToggleSwitch != null)
            {
                SyncAutostartToggleSwitch.IsOn = SyncServiceController.Instance.IsAutostartEnabled();
            }
            if (SyncOnStudyToggleSwitch != null)
            {
                SyncOnStudyToggleSwitch.IsOn = UserPreferenceService.Instance.GetBool("Sync_AutoSyncOnStudy", true);
            }
        }

        private void UpdateSyncStatusUI(bool isRunning)
        {
            if (SyncServiceToggleSwitch != null && SyncServiceToggleSwitch.IsOn != isRunning)
            {
                SyncServiceToggleSwitch.IsOn = isRunning;
            }

            if (SyncServiceStatusDot != null)
            {
                SyncServiceStatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    isRunning ? Microsoft.UI.Colors.LimeGreen : Microsoft.UI.Colors.Gray);
            }

            if (SyncServiceStatusText != null)
            {
                SyncServiceStatusText.Text = isRunning
                    ? LocalizationService.Instance.GetString("LanSyncStatusRunning", "运行中 (52080)")
                    : LocalizationService.Instance.GetString("LanSyncStatusStopped", "已停止");
            }
        }

        private async void SyncServiceToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts)
            {
                bool target = ts.IsOn;
                bool current = await SyncServiceController.Instance.IsServiceRunningAsync();
                if (target && !current)
                {
                    await SyncServiceController.Instance.StartServiceAsync();
                }
                else if (!target && current)
                {
                    await SyncServiceController.Instance.StopServiceAsync();
                }
                await UpdateSyncServiceUIAsync();
            }
        }

        private void SyncAutostartToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts)
            {
                SyncServiceController.Instance.SetAutostart(ts.IsOn);
            }
        }

        private void SyncOnStudyToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts)
            {
                DatabaseService.IsAutoSyncOnStudyEnabled = ts.IsOn;
                UserPreferenceService.Instance.SetBool("Sync_AutoSyncOnStudy", ts.IsOn);
            }
        }

        private async void GeneratePairingCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 电脑端生成长效配对码（10年有效，直到手机端核销），彻底告别倒计时焦虑
                string code = await _dbService.CreatePairingCodeAsync(TimeSpan.FromDays(3650));
                if (code.Length == 6)
                {
                    PairingCodeTextBlock.Text = $"{code.Substring(0, 3)} {code.Substring(3)}";
                }
                else
                {
                    PairingCodeTextBlock.Text = code;
                }
                PairingCodeTextBlock.Opacity = 1.0;
                PairingCodeDisplayPanel.Visibility = Visibility.Visible;

                // 显示电脑本机当前活动局域网 IP
                var endpoints = LanSyncService.GetNetworkEndpoints();
                string primaryIp = endpoints.FirstOrDefault(ep => !ep.IpAddress.StartsWith("127."))?.IpAddress ?? "127.0.0.1";
                if (PairingServerEndpointTextBlock != null)
                {
                    PairingServerEndpointTextBlock.Text = $"本机服务地址: {primaryIp}:52080";
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.GeneratePairingCode_Click");
            }
        }

        private void CopyPairingCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = PairingCodeTextBlock?.Text?.Replace(" ", string.Empty) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text) && text != "------")
                {
                    var pkg = new DataPackage();
                    pkg.SetText(text);
                    Clipboard.SetContent(pkg);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.CopyPairingCode_Click");
            }
        }

        private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
        {
            await LoadPairedDevicesAsync();
        }

        private async Task LoadPairedDevicesAsync()
        {
            try
            {
                var devices = await _dbService.GetPairedDevicesAsync();
                try
                {
                    var liveDevices = await SyncServiceController.Instance.GetConnectedDevicesStatusAsync();
                    var liveMap = liveDevices.ToDictionary(d => d.Id, d => d);
                    foreach (var d in devices)
                    {
                        if (liveMap.TryGetValue(d.Id, out var live))
                        {
                            d.IsOnline = live.IsOnline;
                            if (!string.IsNullOrWhiteSpace(live.ClientIp)) d.ClientIp = live.ClientIp;
                            if (!string.IsNullOrWhiteSpace(live.DeviceModel)) d.DeviceModel = live.DeviceModel;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SettingsPage.LoadPairedDevicesAsync.LiveStatus");
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (devices != null && devices.Count > 0)
                    {
                        if (NoDevicesPanel != null) NoDevicesPanel.Visibility = Visibility.Collapsed;
                        if (PairedDevicesListView != null)
                        {
                            PairedDevicesListView.Visibility = Visibility.Visible;
                            PairedDevicesListView.ItemsSource = null;
                            PairedDevicesListView.ItemsSource = devices;
                        }
                    }
                    else
                    {
                        if (NoDevicesPanel != null) NoDevicesPanel.Visibility = Visibility.Visible;
                        if (PairedDevicesListView != null)
                        {
                            PairedDevicesListView.Visibility = Visibility.Collapsed;
                            PairedDevicesListView.ItemsSource = null;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SettingsPage.LoadPairedDevicesAsync");
            }
        }

        private async void PullFromSpecificDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string deviceId)
            {
                btn.IsEnabled = false;
                try
                {
                    var loc = LocalizationService.Instance;
                    bool isRunning = await SyncServiceController.Instance.IsServiceRunningAsync();
                    if (!isRunning)
                    {
                        var tip = new ContentDialog
                        {
                            Title = loc.GetString("SyncServiceNotRunningTitle", "局域网同步服务未启动"),
                            Content = DialogHelper.CreateTextBlockContent(loc.GetString("SyncServiceNotRunningMsg", "局域网同步服务当前未运行，请先在上方开启【局域网同步守护服务】后再执行拉取。")),
                            CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                        };
                        MainWindow.RegisterActiveDialog(tip);
                        await tip.ShowAsync();
                        MainWindow.UnregisterActiveDialog(tip);
                        return;
                    }

                    var devices = await _dbService.GetPairedDevicesAsync();
                    var targetDevice = devices.FirstOrDefault(d => d.Id == deviceId);
                    string devName = targetDevice?.DisplayTitle ?? targetDevice?.DeviceName ?? "Mobile";

                    // 1. 先生成独立安全快照 (pre_pull)
                    await _dbService.CreatePrePullBackupAsync(devName);
                    await RefreshBackupsListAsync();

                    // 2. 发起定向拉取指令
                    var (success, notified, msg) = await SyncServiceController.Instance.RequestMobilePushAsync(deviceId);

                    string displayMsg = success
                        ? (notified > 0
                            ? string.Format(loc.GetString("PullTargetSentMsg", "已成功向设备【{0}】发送全量推送指令。\n手机端将在数秒内将最新全量词库及复习日志推回电脑并安全合并。"), devName)
                            : string.Format(loc.GetString("PullTargetOfflineMsg", "设备【{0}】当前未与电脑建立实时连接。\n请在手机上打开 Tampo 并保持在同一 WiFi 局域网下，然后重试。"), devName))
                        : string.Format(loc.GetString("PullRequestFailedMsg", "发送拉取指令失败：{0}"), msg);

                    var resultDialog = new ContentDialog
                    {
                        Title = success ? loc.GetString("PullRequestSuccessTitle", "已发送拉取请求") : loc.GetString("PullRequestFailedTitle", "拉取请求未完成"),
                        Content = DialogHelper.CreateTextBlockContent(displayMsg),
                        CloseButtonText = loc.GetString("ButtonClose", "关闭"),
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };
                    MainWindow.RegisterActiveDialog(resultDialog);
                    await resultDialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(resultDialog);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SettingsPage.PullFromSpecificDevice_Click");
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void EditDeviceAlias_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string deviceId)
            {
                try
                {
                    var loc = LocalizationService.Instance;
                    var devices = await _dbService.GetPairedDevicesAsync();
                    var targetDevice = devices.FirstOrDefault(d => d.Id == deviceId);
                    if (targetDevice == null) return;

                    var inputBox = new TextBox
                    {
                        Text = targetDevice.CustomAlias ?? targetDevice.DeviceName,
                        PlaceholderText = loc.GetString("DeviceAliasPlaceholder", "例如：我的主力机、小米 13..."),
                        MaxLength = 30
                    };

                    var dialog = new ContentDialog
                    {
                        Title = loc.GetString("EditDeviceAliasTitle", "设置设备备注名称"),
                        Content = inputBox,
                        PrimaryButtonText = loc.GetString("ButtonSave", "保存"),
                        CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };

                    MainWindow.RegisterActiveDialog(dialog);
                    var res = await dialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(dialog);

                    if (res == ContentDialogResult.Primary)
                    {
                        string newAlias = inputBox.Text?.Trim() ?? string.Empty;
                        await _dbService.UpdateDeviceCustomAliasAsync(deviceId, newAlias);
                        await LoadPairedDevicesAsync();
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SettingsPage.EditDeviceAlias_Click");
                }
            }
        }

        private async void UnbindDevice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string deviceId)
            {
                try
                {
                    var loc = LocalizationService.Instance;
                    var confirmDialog = new ContentDialog
                    {
                        Title = loc.GetString("UnbindDialogTitle", "确认解除设备绑定？"),
                        Content = DialogHelper.CreateTextBlockContent(loc.GetString("UnbindDialogContent", "解除绑定后，该手机设备将无法再同步数据，后续同步请求将返回 401 鉴权失败。\n\n是否确认解除绑定？")),
                        PrimaryButtonText = loc.GetString("ButtonUnbind", "解除绑定"),
                        CloseButtonText = loc.GetString("ButtonCancel", "取消"),
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = Converters.ThemeHelper.GetActualTheme()
                    };

                    MainWindow.RegisterActiveDialog(confirmDialog);
                    var res = await confirmDialog.ShowAsync();
                    MainWindow.UnregisterActiveDialog(confirmDialog);

                    if (res == ContentDialogResult.Primary)
                    {
                        await _dbService.DeletePairedDeviceAsync(deviceId);
                        await LoadPairedDevicesAsync();
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "SettingsPage.UnbindDevice_Click");
                }
            }
        }

        #endregion
    }

    public class BackupSnapshotItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FormattedTime { get; set; } = string.Empty;
        public string FormattedSize { get; set; } = string.Empty;
        public string BadgeText { get; set; } = string.Empty;
        public DateTime CreationTime { get; set; }
    }
}
