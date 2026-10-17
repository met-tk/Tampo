using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace NihongoVocab.Installer
{
    public partial class UninstallWindow : Window
    {
        private string _appDirectory;
        private readonly bool _isTempRunner;
        private readonly InstallerLocalization _loc;

        public UninstallWindow()
        {
            _loc = InstallerLocalization.Current;
            this.DataContext = _loc;

            string[] args = Environment.GetCommandLineArgs();
            string? passedTargetDir = null;
            bool isRunner = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--temp-runner", StringComparison.OrdinalIgnoreCase))
                {
                    isRunner = true;
                }
                else if (args[i].Equals("--target-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    passedTargetDir = args[i + 1].Trim('\"');
                }
            }

            _isTempRunner = isRunner;
            string currentBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            if (!string.IsNullOrEmpty(passedTargetDir) && Directory.Exists(passedTargetDir))
            {
                _appDirectory = passedTargetDir;
            }
            else
            {
                _appDirectory = currentBaseDir;
            }

            // 脱壳运行架构：如果当前进程直接在安装目录下运行，克隆到系统 Temp 独立沙箱中启动，
            // 使得原安装目录没有任何进程锁定，从而实现 100% 毫无残留地删除整个安装目录与所有文件！
            if (!_isTempRunner)
            {
                try
                {
                    using var currentProc = Process.GetCurrentProcess();
                    string currentExe = currentProc.MainModule?.FileName ?? "";
                    if (File.Exists(currentExe))
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), $"Tampo_Uninstall_{Guid.NewGuid():N}");
                        Directory.CreateDirectory(tempDir);
                        string runnerExe = Path.Combine(tempDir, "UninstallRunner.exe");
                        File.Copy(currentExe, runnerExe, overwrite: true);

                        var psi = new ProcessStartInfo
                        {
                            FileName = runnerExe,
                            WorkingDirectory = tempDir,
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add("--uninstall");
                        psi.ArgumentList.Add("--temp-runner");
                        psi.ArgumentList.Add("--target-dir");
                        psi.ArgumentList.Add(_appDirectory);
                        Process.Start(psi);

                        // 立即退出原安装目录下的卸载进程
                        Environment.Exit(0);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to spawn temp runner: {ex.Message}");
                }
            }

            InitializeComponent();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmStepPanel.Visibility = Visibility.Collapsed;
            ProgressStepPanel.Visibility = Visibility.Visible;

            bool keepData = KeepUserDataCheckBox.IsChecked == true;

            var result = await Task.Run(() => PerformUninstall(keepData));

            ProgressStepPanel.Visibility = Visibility.Collapsed;
            if (result.Success)
            {
                FinishStepPanel.Visibility = Visibility.Visible;
                if (result.CleanupIncomplete)
                    MessageBox.Show("Tampo was removed, but some user data or shortcuts could not be deleted.",
                        "Tampo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                ConfirmStepPanel.Visibility = Visibility.Visible;
                MessageBox.Show("Uninstallation could not complete. The installed files and registration were left in place where possible.",
                    "Tampo", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private (bool Success, bool CleanupIncomplete) PerformUninstall(bool keepData)
        {
            try
            {
                bool cleanupIncomplete = false;
                if (!IsSafeDirectoryToDelete(_appDirectory))
                    throw new InvalidDataException("Uninstallation target is not a verified Tampo installation.");

                UpdateProgress(15, _loc.UninstallingTitle);

                // 1. 关闭任何可能还在运行的 Tampo / NihongoVocab 进程
                try
                {
                    string targetPrefix = Path.GetFullPath(_appDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    foreach (var name in new[] { "Tampo", "NihongoVocab", "TampoSyncService" })
                    {
                        foreach (var p in Process.GetProcessesByName(name))
                        {
                            try
                            {
                                string? path = p.MainModule?.FileName;
                                if (p.Id != Environment.ProcessId && path != null &&
                                    path.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                                {
                                    p.Kill();
                                    p.WaitForExit(3000);
                                }
                            }
                            catch { }
                            finally { p.Dispose(); }
                        }
                    }
                }
                catch { cleanupIncomplete = true; }

                UpdateProgress(35, _loc.UninstallingTitle);

                // 2. 清理系统快捷方式
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tampo.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tampo.lnk");
                    if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);
                }
                catch { cleanupIncomplete = true; }

                UpdateProgress(55, _loc.UninstallingTitle);

                // 先移除程序目录；若失败，保留用户数据供重试。
                if (!ForceDeleteDirectory(_appDirectory))
                    throw new IOException("The installation directory could not be removed.");

                // 清理本地缓存数据（若用户不保留）
                if (!keepData)
                {
                    UpdateProgress(70, _loc.UninstallingTitle);
                    try
                    {
                        string localAppDataNihongo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NihongoVocab");
                        if (Directory.Exists(localAppDataNihongo))
                        {
                            if (!ForceDeleteDirectory(localAppDataNihongo, userDataDirectory: true))
                                cleanupIncomplete = true;
                        }

                        string localAppDataTampo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tampo");
                        if (Directory.Exists(localAppDataTampo))
                        {
                            if (!ForceDeleteDirectory(localAppDataTampo, userDataDirectory: true))
                                cleanupIncomplete = true;
                        }
                    }
                    catch { cleanupIncomplete = true; }
                }

                // 只有程序目录确实移除后，才注销卸载项，便于失败时重试。
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tampo", throwOnMissingSubKey: false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab", throwOnMissingSubKey: false);
                }
                catch { cleanupIncomplete = true; }

                UpdateProgress(100, _loc.UninstallSuccess);
                return (true, cleanupIncomplete);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Uninstall error: {ex.Message}");
                return (false, false);
            }
        }

        /// <summary>
        /// 校验待删除目录是否为合法的 Tampo / NihongoVocab 专属目录，防止在根目录或系统目录误执行递归删除
        /// </summary>
        private static bool IsSafeDirectoryToDelete(string targetDir, bool userDataDirectory = false)
        {
            if (string.IsNullOrWhiteSpace(targetDir)) return false;
            try
            {
                string fullPath = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string? rootPath = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrEmpty(fullPath) || string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                if (!Directory.Exists(fullPath) ||
                    (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    return false;

                if (userDataDirectory)
                {
                    string localData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(fullPath, Path.Combine(localData, "Tampo"), StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(fullPath, Path.Combine(localData, "NihongoVocab"), StringComparison.OrdinalIgnoreCase);
                }

                string leafName = Path.GetFileName(fullPath);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, desktop, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fullPath, profile, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                bool hasExecutable = File.Exists(Path.Combine(fullPath, "Tampo.exe")) ||
                                     File.Exists(Path.Combine(fullPath, "NihongoVocab.exe"));
                if (!hasExecutable) return false;

                if (string.Equals(leafName, "Tampo", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(leafName, "NihongoVocab", StringComparison.OrdinalIgnoreCase))
                    return true;

                // 自定义旧安装目录须与注册表记录一致。
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tampo")
                    ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab");
                string? registered = key?.GetValue("InstallLocation") as string;
                return !string.IsNullOrWhiteSpace(registered) &&
                       string.Equals(fullPath, Path.GetFullPath(registered).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 彻底删除指定目录（深度递归清除只读、隐藏属性，多轮重试直到目录完全消失）
        /// </summary>
        private static bool ForceDeleteDirectory(string targetDir, bool userDataDirectory = false)
        {
            if (!Directory.Exists(targetDir)) return true;
            if (!IsSafeDirectoryToDelete(targetDir, userDataDirectory)) return false;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    DeleteTreeWithoutFollowingLinks(new DirectoryInfo(targetDir));
                    if (!Directory.Exists(targetDir))
                    {
                        return true;
                    }
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            return !Directory.Exists(targetDir);
        }

        private static void DeleteTreeWithoutFollowingLinks(DirectoryInfo directory)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                directory.Delete();
                return;
            }
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                    DeleteTreeWithoutFollowingLinks(child);
                else
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        entry.Attributes = FileAttributes.Normal;
                    entry.Delete();
                }
            }
            directory.Delete();
        }

        private void UpdateProgress(int percent, string status)
        {
            Dispatcher.Invoke(() =>
            {
                UninstallProgressBar.Value = percent;
                ProgressStatusTextBlock.Text = status;
            });
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            // 如果是在 TempRunner 中运行，安排在进程退出后清理临时沙箱
            if (_isTempRunner)
            {
                try
                {
                    string currentTempDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                    string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/');
                    string fullTempDir = Path.GetFullPath(currentTempDir);
                    string leaf = Path.GetFileName(fullTempDir);
                    bool isOwnTempDir = string.Equals(Path.GetDirectoryName(fullTempDir), tempRoot, StringComparison.OrdinalIgnoreCase) &&
                        leaf.StartsWith("Tampo_Uninstall_", StringComparison.Ordinal) &&
                        Guid.TryParseExact(leaf.Substring("Tampo_Uninstall_".Length), "N", out _) &&
                        File.Exists(Path.Combine(fullTempDir, "UninstallRunner.exe"));
                    if (isOwnTempDir)
                    {
                        string tempBat = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}.bat");
                        string script = $@"@echo off
chcp 65001 >nul
ping 127.0.0.1 -n 2 >nul
rd /s /q ""{fullTempDir}""
del ""%~f0""
";
                        File.WriteAllText(tempBat, script, new System.Text.UTF8Encoding(false));
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c \"\"{tempBat}\"\"",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });
                    }
                }
                catch { }
            }

            this.Close();
        }
    }
}
