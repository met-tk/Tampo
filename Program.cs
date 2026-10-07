using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NihongoVocab.Services;

namespace NihongoVocab
{
    public static class Program
    {
        private const string MutexName = @"Local\Tampo_App_SingleInstance_Mutex_2026";
        private static Mutex? _singleInstanceMutex;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        [STAThread]
        public static void Main(string[] args)
        {
            bool isNewInstance = false;
            try
            {
                _singleInstanceMutex = new Mutex(true, MutexName, out isNewInstance);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "Program.Main: Mutex creation");
                isNewInstance = true;
            }

            if (!isNewInstance)
            {
                // 已有实例在运行，尝试将现有窗口恢复并激活置顶
                CrashLogger.LogInfo("Tampo duplicate instance detected, checking existing window.");
                bool activated = ActivateExistingWindow();
                if (activated)
                {
                    CrashLogger.LogInfo("Existing window successfully activated, exiting current launcher.");
                    return;
                }

                // 若未发现有效窗口，说明旧进程属于残留/僵尸进程，自动清理后启动全新窗口
                CrashLogger.LogInfo("Existing process has no visible window. Terminating stale process to launch freshly.");
                KillStaleInstances();
            }

            try
            {
                global::WinRT.ComWrappersSupport.InitializeComWrappers();
                global::Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                        global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                    global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "Program.Main: Application.Start");
                throw;
            }
            finally
            {
                if (_singleInstanceMutex != null)
                {
                    try
                    {
                        _singleInstanceMutex.ReleaseMutex();
                        _singleInstanceMutex.Dispose();
                    }
                    catch { }
                }
            }
        }

        private static void KillStaleInstances()
        {
            try
            {
                var currentProc = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProc.ProcessName);
                foreach (var proc in processes)
                {
                    if (proc.Id != currentProc.Id)
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                }
            }
            catch { }
        }

        private static bool ActivateExistingWindow()
        {
            try
            {
                var currentProc = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProc.ProcessName);

                foreach (var proc in processes)
                {
                    if (proc.Id == currentProc.Id) continue;

                    IntPtr targetHwnd = proc.MainWindowHandle;
                    if (targetHwnd == IntPtr.Zero)
                    {
                        EnumWindows((hWnd, lParam) =>
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);
                            if (pid == proc.Id)
                            {
                                targetHwnd = hWnd;
                                return false;
                            }
                            return true;
                        }, IntPtr.Zero);
                    }

                    if (targetHwnd != IntPtr.Zero)
                    {
                        if (IsIconic(targetHwnd))
                        {
                            ShowWindow(targetHwnd, SW_RESTORE);
                        }
                        ShowWindow(targetHwnd, SW_SHOW);
                        SetForegroundWindow(targetHwnd);
                        SwitchToThisWindow(targetHwnd, true);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "Program.ActivateExistingWindow");
            }

            return false;
        }
    }
}
