using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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
        // ─── 常量与元数据 ────────────────────────────────────────────────────────
        private static readonly string NewVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.5.3";
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
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey)
                             ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab");
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
                _targetDirectory = NormalizeSafeInstallDirectory(dialog.FolderName);
                InstallPathTextBox.Text = _targetDirectory;
            }
        }

        /// <summary>
        /// 规范化安装目录，确保手动输入的路径也必定落入独立的 \Tampo（或既有 \NihongoVocab）子文件夹中，
        /// 杜绝直接将磁盘根目录或桌面等父目录作为安装目录导致清理时误删其他文件。
        /// </summary>
        private static string NormalizeSafeInstallDirectory(string rawPath)
        {
            string fullPath = Path.GetFullPath(rawPath.Trim());
            string? rootPath = Path.GetPathRoot(fullPath);
            if (!string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
                fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leafName = Path.GetFileName(fullPath);
            if (!string.Equals(leafName, "Tampo", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(leafName, "NihongoVocab", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = Path.Combine(fullPath, "Tampo");
            }
            return fullPath;
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

            try
            {
                _targetDirectory = NormalizeSafeInstallDirectory(installPath);
                InstallPathTextBox.Text = _targetDirectory;
            }
            catch
            {
                MessageBox.Show("Please specify a valid installation path.", "Tampo",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
            var procs = new List<Process>();
            var procIds = new HashSet<int>();

            bool AddProcess(Process p)
            {
                try
                {
                    if (p.Id != Environment.ProcessId && procIds.Add(p.Id))
                    {
                        procs.Add(p);
                        return true;
                    }
                }
                catch { }
                return false;
            }

            // 仅关闭当前安装目录内的程序，避免误关同名的其他应用。
            if (!string.IsNullOrWhiteSpace(_targetDirectory) && Directory.Exists(_targetDirectory))
            {
                string normTargetDir = Path.GetFullPath(_targetDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        string? modulePath = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(modulePath) &&
                            modulePath.StartsWith(normTargetDir, StringComparison.OrdinalIgnoreCase))
                        {
                            if (AddProcess(p)) continue;
                        }
                    }
                    catch { }
                    p.Dispose();
                }
            }

            if (procs.Count == 0) return true;

            string msg = "Tampo 及其后台同步服务正在运行。是否立即关闭它们以继续安装？";

            var result = MessageBox.Show(msg, "Tampo",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                foreach (var p in procs) p.Dispose();
                return false;
            }

            await Task.Run(() =>
            {
                // 先尝试优雅退出守护进程
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
                    http.PostAsync("http://127.0.0.1:52080/api/control/stop", null).Wait();
                }
                catch { }

                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                // 留出 300ms 保证 Windows 内核完全释放文件句柄
                Thread.Sleep(300);
            });
            return true;
        }

        // ─── 核心安装流程 ──────────────────────────────────────────────────────────
        private bool PerformInstall()
        {
            string? stagingDirectory = null;
            string? previousDirectory = null;
            bool installed = false;
            try
            {
                UpdateProgress(5, _loc.StatusExtracting);
                // 先验证并完整解压新版本，再触碰现有安装目录。
                if (Directory.Exists(_targetDirectory) &&
                    (File.GetAttributes(_targetDirectory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Installation directory cannot be a symbolic link or junction.");
                if (Directory.Exists(_targetDirectory) &&
                    Directory.EnumerateFileSystemEntries(_targetDirectory).Any() &&
                    !File.Exists(Path.Combine(_targetDirectory, "Tampo.exe")) &&
                    !File.Exists(Path.Combine(_targetDirectory, "NihongoVocab.exe")) &&
                    !File.Exists(Path.Combine(_targetDirectory, "Uninstall.exe")))
                    throw new InvalidDataException("Installation directory contains files unrelated to Tampo.");

                string parentDirectory = Path.GetDirectoryName(_targetDirectory)
                    ?? throw new InvalidDataException("Invalid installation directory.");
                Directory.CreateDirectory(parentDirectory);
                stagingDirectory = Path.Combine(parentDirectory, $".Tampo_stage_{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDirectory);

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
                    throw new FileNotFoundException("Missing payload.zip resource.");

                string fullTargetRoot = Path.GetFullPath(stagingDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                using (zipStream)
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    if (archive.Entries.Count == 0)
                        throw new InvalidDataException("Installer payload is empty.");
                    const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
                    long extractedBytes = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length > MaxExtractedBytes - extractedBytes)
                            throw new InvalidDataException("Installer payload exceeds the maximum extracted size.");
                        extractedBytes += entry.Length;
                    }
                    int total = Math.Max(1, archive.Entries.Count);
                    int done = 0;
                    foreach (var entry in archive.Entries)
                    {
                        string dest = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
                        if (!dest.StartsWith(fullTargetRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException($"Blocked unsafe zip entry path: {entry.FullName}");
                        }

                        if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                        {
                            Directory.CreateDirectory(dest);
                            continue;
                        }

                        string? dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        entry.ExtractToFile(dest, overwrite: true);
                        done++;
                        int pct = 20 + (int)(done / (double)total * 55);
                        UpdateProgress(pct, $"{_loc.StatusExtracting} ({done}/{total})");
                    }
                }

                if (!File.Exists(Path.Combine(stagingDirectory, "Tampo.exe")) &&
                    !File.Exists(Path.Combine(stagingDirectory, "NihongoVocab.exe")))
                    throw new InvalidDataException("Installer payload does not contain the application executable.");

                // 同卷目录重命名可在新版本准备完成后切换，并可在后续配置失败时恢复旧版。
                UpdateProgress(78, _loc.CleaningOldFiles);
                if (Directory.Exists(_targetDirectory))
                {
                    previousDirectory = Path.Combine(parentDirectory, $".Tampo_previous_{Guid.NewGuid():N}");
                    Directory.Move(_targetDirectory, previousDirectory);
                }
                Directory.Move(stagingDirectory, _targetDirectory);
                stagingDirectory = null;

                UpdateProgress(80, _loc.StatusConfiguring);
                using var currentProcess = Process.GetCurrentProcess();
                string selfExe = currentProcess.MainModule?.FileName ?? "";
                string uninstallExe = Path.Combine(_targetDirectory, "Uninstall.exe");
                if (File.Exists(selfExe)) File.Copy(selfExe, uninstallExe, overwrite: true);

                UpdateProgress(88, _loc.StatusConfiguring);
                string mainExe = Path.Combine(_targetDirectory, "Tampo.exe");
                if (!File.Exists(mainExe))
                {
                    string legacyExe = Path.Combine(_targetDirectory, "NihongoVocab.exe");
                    if (File.Exists(legacyExe)) mainExe = legacyExe;
                }
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

                UpdateProgress(95, _loc.StatusConfiguring);
                RegisterWindowsUninstallInfo(mainExe, uninstallExe);

                installed = true;
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
            finally
            {
                try
                {
                    if (stagingDirectory != null && Directory.Exists(stagingDirectory))
                        DeleteDirectoryWithoutFollowingLinks(stagingDirectory);

                    if (previousDirectory != null && Directory.Exists(previousDirectory))
                    {
                        if (installed)
                        {
                            DeleteDirectoryWithoutFollowingLinks(previousDirectory);
                        }
                        else
                        {
                            if (Directory.Exists(_targetDirectory))
                                DeleteDirectoryWithoutFollowingLinks(_targetDirectory);
                            Directory.Move(previousDirectory, _targetDirectory);
                        }
                    }
                }
                catch (Exception cleanupError)
                {
                    Debug.WriteLine($"Installation cleanup or rollback failed: {cleanupError}");
                }
            }
        }

        /// <summary>删除安装器自己创建的暂存目录，不穿越符号链接或目录联接。</summary>
        private static void DeleteDirectoryWithoutFollowingLinks(string dir)
        {
            var root = new DirectoryInfo(dir);
            if (!root.Exists) return;
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                root.Delete();
                return;
            }
            foreach (var entry in root.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    DeleteDirectoryWithoutFollowingLinks(child.FullName);
                }
                else
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }
            root.Delete();
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
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab",
                        throwOnMissingSubKey: false);
                }
                catch { }
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
                string mainExe = Path.Combine(_targetDirectory, "Tampo.exe");
                if (!File.Exists(mainExe))
                {
                    string legacyExe = Path.Combine(_targetDirectory, "NihongoVocab.exe");
                    if (File.Exists(legacyExe)) mainExe = legacyExe;
                }
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

