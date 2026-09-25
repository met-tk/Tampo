using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace NihongoVocab.Installer
{
    /// <summary>安装状态枚举</summary>
    public enum InstallState
    {
        /// <summary>全新安装</summary>
        Fresh,
        /// <summary>升级安装（新版 > 已安装版本）</summary>
        Upgrade,
        /// <summary>重装修复（新版 == 已安装版本）</summary>
        Reinstall,
        /// <summary>降级安装（新版 < 已安装版本）</summary>
        Downgrade
    }

    public partial class MainWindow : Window
    {
        // ─── 常量 ────────────────────────────────────────────────────────────────
        private const string NewVersion = "1.1.0";
        private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tampo";

        // ─── 字段 ────────────────────────────────────────────────────────────────
        private string _targetDirectory;
        private readonly InstallerLocalization _loc;
        private InstallState _installState = InstallState.Fresh;
        private string? _existingVersion;
        private string? _existingInstallPath;

        // ─── 构造函数 ─────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            _loc = InstallerLocalization.Current;
            this.DataContext = _loc;

            // 1. 检查注册表中是否存在已安装版本
            DetectExistingInstallation();

            // 2. 决定默认路径
            if (!string.IsNullOrEmpty(_existingInstallPath) && Directory.Exists(_existingInstallPath))
            {
                _targetDirectory = _existingInstallPath;
            }
            else
            {
                _targetDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Tampo");
            }
            InstallPathTextBox.Text = _targetDirectory;

            // 3. 根据安装状态更新 UI
            ApplyInstallStateUI();
        }

        // ─── 已安装检测 ────────────────────────────────────────────────────────────
        private void DetectExistingInstallation()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key == null) return;

                _existingVersion = key.GetValue("DisplayVersion") as string;
                _existingInstallPath = key.GetValue("InstallLocation") as string;

                if (string.IsNullOrEmpty(_existingVersion)) return;

                int cmp = CompareVersions(NewVersion, _existingVersion);
                if (cmp > 0) _installState = InstallState.Upgrade;
                else if (cmp == 0) _installState = InstallState.Reinstall;
                else _installState = InstallState.Downgrade;
            }
            catch { }
        }

        /// <summary>简单版本字符串比较（主.次.修），返回正数代表 a > b</summary>
        private static int CompareVersions(string a, string b)
        {
            try
            {
                var va = Version.Parse(a.Trim());
                var vb = Version.Parse(b.Trim());
                return va.CompareTo(vb);
            }
            catch
            {
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        // ─── 根据状态更新界面 ──────────────────────────────────────────────────────
        private void ApplyInstallStateUI()
        {
            switch (_installState)
            {
                case InstallState.Fresh:
                    // 全新安装——默认状态，无需改变任何 UI
                    break;

                case InstallState.Upgrade:
                    ShowStatusBanner(
                        icon: "↑",
                        bgColor: "#EFF6FF",
                        borderColor: "#BFDBFE",
                        textColor: "#1D4ED8",
                        title: _loc.UpgradeBannerTitle,
                        versionLine: $"v{_existingVersion}  →  v{NewVersion}",
                        pathLine: $"{_loc.InstalledAt}: {_existingInstallPath}");
                    MainActionButton.Content = _loc.UpgradeButton;
                    MainActionButton.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)); // green-600
                    break;

                case InstallState.Reinstall:
                    ShowStatusBanner(
                        icon: "↺",
                        bgColor: "#FFFBEB",
                        borderColor: "#FDE68A",
                        textColor: "#92400E",
                        title: _loc.ReinstallBannerTitle,
                        versionLine: $"v{_existingVersion}",
                        pathLine: $"{_loc.InstalledAt}: {_existingInstallPath}");
                    MainActionButton.Content = _loc.ReinstallButton;
                    break;

                case InstallState.Downgrade:
                    ShowStatusBanner(
                        icon: "⚠",
                        bgColor: "#FFF7ED",
                        borderColor: "#FDBA74",
                        textColor: "#C2410C",
                        title: _loc.DowngradeBannerTitle,
                        versionLine: $"v{_existingVersion}  →  v{NewVersion}",
                        pathLine: $"{_loc.InstalledAt}: {_existingInstallPath}");
                    MainActionButton.Content = _loc.DowngradeButton;
                    MainActionButton.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)); // orange-600
                    break;
            }
        }

        private void ShowStatusBanner(string icon, string bgColor, string borderColor,
                                       string textColor, string title,
                                       string versionLine, string pathLine)
        {
            var bg = (SolidColorBrush)new BrushConverter().ConvertFromString(bgColor)!;
            var border = (SolidColorBrush)new BrushConverter().ConvertFromString(borderColor)!;
            var text = (SolidColorBrush)new BrushConverter().ConvertFromString(textColor)!;

            StatusBannerBorder.Background = bg;
            StatusBannerBorder.BorderBrush = border;
            StatusBannerIcon.Text = icon;
            StatusBannerIcon.Foreground = text;
            StatusBannerTitle.Text = title;
            StatusBannerTitle.Foreground = text;
            StatusBannerVersion.Text = versionLine;
            StatusBannerVersion.Foreground = text;
            StatusBannerPath.Text = pathLine;
            StatusBannerPath.Foreground = text;
            StatusBannerBorder.Visibility = Visibility.Visible;
        }

        // ─── 窗口控制 ───────────────────────────────────────────────────────────────
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => this.WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => this.Close();

        // ─── 浏览按钮 ─────────────────────────────────────────────────────────────
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            string parentHint = Directory.Exists(_targetDirectory)
                ? Path.GetDirectoryName(_targetDirectory) ?? _targetDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            var dialog = new OpenFolderDialog
            {
                Title = _loc.InstallPathLabel,
                InitialDirectory = parentHint
            };

            if (dialog.ShowDialog() == true)
            {
                // 强制追加 \Tampo 子目录，不污染父目录
                _targetDirectory = Path.Combine(dialog.FolderName, "Tampo");
                InstallPathTextBox.Text = _targetDirectory;
            }
        }

        // ─── 安装按钮 ─────────────────────────────────────────────────────────────
        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            string installPath = InstallPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(installPath))
            {
                MessageBox.Show("Please specify a valid installation path.", "Tampo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _targetDirectory = installPath;

            // 关闭正在运行的 Tampo 进程
            if (!await PromptAndKillRunningProcess()) return;

            SetupStepPanel.Visibility = Visibility.Collapsed;
            ProgressStepPanel.Visibility = Visibility.Visible;

            bool success = await Task.Run(() => PerformInstall());

            ProgressStepPanel.Visibility = Visibility.Collapsed;
            if (success)
            {
                FinishStepPanel.Visibility = Visibility.Visible;
            }
            else
            {
                SetupStepPanel.Visibility = Visibility.Visible;
            }
        }

        private async Task<bool> PromptAndKillRunningProcess()
        {
            var procs = Process.GetProcessesByName("NihongoVocab");
            if (procs.Length == 0) return true;

            string msg = _installState == InstallState.Upgrade
                ? $"Tampo (v{_existingVersion}) is running. Close it to continue?"
                : "Tampo is running. Close it to continue?";

            var result = MessageBox.Show(msg, "Tampo",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return false;

            await Task.Run(() =>
            {
                foreach (var p in procs)
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
            });
            return true;
        }

        // ─── 核心安装流程 ──────────────────────────────────────────────────────────
        private bool PerformInstall()
        {
            try
            {
                UpdateProgress(5, _loc.StatusExtracting);

                // ① 创建目标目录
                if (!Directory.Exists(_targetDirectory))
                    Directory.CreateDirectory(_targetDirectory);

                // ② 升级 / 重装时：先清理旧文件，确保无孤立残留
                if (_installState is InstallState.Upgrade or InstallState.Reinstall or InstallState.Downgrade)
                {
                    UpdateProgress(10, _loc.CleaningOldFiles);
                    CleanOldProgramFiles(_targetDirectory);
                }

                UpdateProgress(20, _loc.StatusExtracting);

                // ③ 获取 payload.zip（内嵌资源 → 同目录回退）
                Stream? zipStream = null;
                var assembly = Assembly.GetExecutingAssembly();
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipStream = assembly.GetManifestResourceStream(name);
                        break;
                    }
                }
                if (zipStream == null)
                {
                    string localZip = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload.zip");
                    if (File.Exists(localZip)) zipStream = File.OpenRead(localZip);
                }
                if (zipStream == null)
                    throw new FileNotFoundException("Missing payload.zip resource.");

                // ④ 解压所有文件
                using (zipStream)
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    int total = archive.Entries.Count;
                    int done = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/"))
                        {
                            Directory.CreateDirectory(Path.Combine(_targetDirectory, entry.FullName));
                            continue;
                        }
                        string dest = Path.Combine(_targetDirectory, entry.FullName);
                        string? dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        entry.ExtractToFile(dest, overwrite: true);
                        done++;
                        int pct = 20 + (int)(done / (double)total * 55);
                        UpdateProgress(pct, $"{_loc.StatusExtracting} ({done}/{total})");
                    }
                }

                // ⑤ 复制自身作为 Uninstall.exe
                UpdateProgress(80, _loc.StatusConfiguring);
                string selfExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string uninstallExe = Path.Combine(_targetDirectory, "Uninstall.exe");
                if (File.Exists(selfExe)) File.Copy(selfExe, uninstallExe, overwrite: true);

                // ⑥ 创建快捷方式
                UpdateProgress(88, _loc.StatusConfiguring);
                string mainExe = Path.Combine(_targetDirectory, "NihongoVocab.exe");
                string iconPath = Path.Combine(_targetDirectory, "Assets", "AppIcon.ico");
                if (!File.Exists(iconPath)) iconPath = mainExe;

                Dispatcher.Invoke(() =>
                {
                    if (CreateDesktopShortcutCheckBox.IsChecked == true)
                    {
                        string lnk = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tampo.lnk");
                        ShortcutHelper.CreateShortcut(lnk, mainExe, _targetDirectory,
                            "Tampo - Modern Japanese Vocabulary & FSRS Memory Scheduler", iconPath);
                    }
                    if (CreateStartMenuShortcutCheckBox.IsChecked == true)
                    {
                        string lnk = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tampo.lnk");
                        ShortcutHelper.CreateShortcut(lnk, mainExe, _targetDirectory,
                            "Tampo - Modern Japanese Vocabulary & FSRS Memory Scheduler", iconPath);
                    }
                });

                // ⑦ 写入注册表（含安装/升级时间）
                UpdateProgress(95, _loc.StatusConfiguring);
                RegisterWindowsUninstallInfo(mainExe, uninstallExe);

                UpdateProgress(100, _loc.InstallSuccess);
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Installation error:\n{ex.Message}", "Tampo",
                        MessageBoxButton.OK, MessageBoxImage.Error));
                return false;
            }
        }

        /// <summary>清理旧版本程序文件（保留用户生词库数据目录）</summary>
        private static void CleanOldProgramFiles(string dir)
        {
            if (!Directory.Exists(dir)) return;
            try
            {
                // 删除根目录下所有文件
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                    }
                    catch { }
                }
                // 删除子目录（保留 Assets 图标缓存，会被解压覆盖；整体删除然后重建更彻底）
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { Directory.Delete(d, recursive: true); } catch { }
                }
            }
            catch { }
        }

        // ─── 注册表写入 ────────────────────────────────────────────────────────────
        private void RegisterWindowsUninstallInfo(string mainExe, string uninstallExe)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
                if (key == null) return;

                string installDate = DateTime.Now.ToString("yyyyMMdd");
                key.SetValue("DisplayName", "Tampo");
                key.SetValue("DisplayVersion", NewVersion);
                key.SetValue("Publisher", "taketo");
                key.SetValue("InstallLocation", _targetDirectory);
                key.SetValue("InstallDate", installDate);
                key.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall");
                key.SetValue("QuietUninstallString", $"\"{uninstallExe}\" --uninstall --quiet");
                key.SetValue("DisplayIcon", $"{mainExe},0");
                key.SetValue("EstimatedSize", 185000);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                // 清理旧键名
                try { Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab",
                    throwOnMissingSubKey: false); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Registry write failed: {ex.Message}");
            }
        }

        // ─── 进度更新 ─────────────────────────────────────────────────────────────
        private void UpdateProgress(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                InstallProgressBar.Value = percent;
                ProgressStatusTextBlock.Text = status;
            });
        }

        // ─── 完成按钮 ─────────────────────────────────────────────────────────────
        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            if (LaunchAppCheckBox.IsChecked == true)
            {
                string mainExe = Path.Combine(_targetDirectory, "NihongoVocab.exe");
                if (File.Exists(mainExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = mainExe,
                        WorkingDirectory = _targetDirectory,
                        UseShellExecute = true
                    });
                }
            }
            this.Close();
        }
    }
}
