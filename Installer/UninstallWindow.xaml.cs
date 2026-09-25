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
                    string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (File.Exists(currentExe))
                    {
                        string tempDir = Path.Combine(Path.GetTempPath(), $"Tampo_Uninstall_{Guid.NewGuid():N}");
                        Directory.CreateDirectory(tempDir);
                        string runnerExe = Path.Combine(tempDir, "UninstallRunner.exe");
                        File.Copy(currentExe, runnerExe, overwrite: true);

                        var psi = new ProcessStartInfo
                        {
                            FileName = runnerExe,
                            Arguments = $"--uninstall --temp-runner --target-dir \"{_appDirectory}\"",
                            WorkingDirectory = tempDir,
                            UseShellExecute = true
                        };
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

            await Task.Run(() => PerformUninstall(keepData));

            ProgressStepPanel.Visibility = Visibility.Collapsed;
            FinishStepPanel.Visibility = Visibility.Visible;
        }

        private void PerformUninstall(bool keepData)
        {
            try
            {
                UpdateProgress(15, _loc.UninstallingTitle);

                // 1. 关闭任何可能还在运行的 Tampo / NihongoVocab 进程
                try
                {
                    var procs = Process.GetProcessesByName("NihongoVocab");
                    foreach (var p in procs)
                    {
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                }
                catch { }

                UpdateProgress(35, _loc.UninstallingTitle);

                // 2. 清理系统快捷方式
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tampo.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string startMenuLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tampo.lnk");
                    if (File.Exists(startMenuLnk)) File.Delete(startMenuLnk);
                }
                catch { }

                UpdateProgress(55, _loc.UninstallingTitle);

                // 3. 清理注册表卸载信息
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tampo", throwOnMissingSubKey: false);
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\NihongoVocab", throwOnMissingSubKey: false);
                }
                catch { }

                // 4. 清理本地缓存数据（若用户不保留）
                if (!keepData)
                {
                    UpdateProgress(70, _loc.UninstallingTitle);
                    try
                    {
                        string localAppDataNihongo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NihongoVocab");
                        if (Directory.Exists(localAppDataNihongo))
                        {
                            ForceDeleteDirectory(localAppDataNihongo);
                        }

                        string localAppDataTampo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tampo");
                        if (Directory.Exists(localAppDataTampo))
                        {
                            ForceDeleteDirectory(localAppDataTampo);
                        }
                    }
                    catch { }
                }

                UpdateProgress(85, _loc.UninstallingTitle);

                // 5. 彻底物理销毁整个安装目标目录及其下所有文件和子目录（做到 100% 毫无残留）
                if (!string.IsNullOrEmpty(_appDirectory) && Directory.Exists(_appDirectory))
                {
                    ForceDeleteDirectory(_appDirectory);
                }

                UpdateProgress(100, _loc.UninstallSuccess);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Uninstall error: {ex.Message}");
            }
        }

        /// <summary>
        /// 彻底删除指定目录（深度递归清除只读、隐藏属性，多轮重试直到目录完全消失）
        /// </summary>
        private static void ForceDeleteDirectory(string targetDir)
        {
            if (!Directory.Exists(targetDir)) return;

            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    // 先递归移除所有文件的只读与系统隐藏保护
                    var di = new DirectoryInfo(targetDir);
                    if (di.Exists)
                    {
                        foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                fi.Attributes = FileAttributes.Normal;
                                fi.Delete();
                            }
                            catch { }
                        }
                    }

                    // 递归删除整个目录
                    Directory.Delete(targetDir, recursive: true);

                    if (!Directory.Exists(targetDir))
                    {
                        return; // 成功删除
                    }
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            // 若仍残留，调用底层命令行回退强制销毁
            if (Directory.Exists(targetDir))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c rd /s /q \"{targetDir}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(2000);
                }
                catch { }
            }
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
                    if (currentTempDir.IndexOf("Temp", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string tempBat = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}.bat");
                        string script = $@"@echo off
ping 127.0.0.1 -n 2 >nul
rd /s /q ""{currentTempDir}""
del ""%~f0""
";
                        File.WriteAllText(tempBat, script, System.Text.Encoding.ASCII);
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
