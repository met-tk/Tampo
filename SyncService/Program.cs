#if TAMPO_SYNC_SERVICE
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SQLite;
using NihongoVocab.Services;

namespace NihongoVocab.SyncService
{
    internal static class Program
    {
        private const string MutexName = @"Global\TampoSyncService_SingleInstance_Mutex_2026";

        [STAThread]
        private static async Task<int> Main(string[] args)
        {
            // 1. 全局未捕获异常守护，确保无窗口崩溃时有据可查
            SetupExceptionHandling();

            // 2. 进程状态与互斥体句柄
            bool hasMutex = false;
            Mutex? mutex = null;
            CancellationTokenSource? cts = null;
            ManualResetEventSlim? shutdownCompleted = null;
            EventHandler? processExitHandler = null;
            ConsoleCancelEventHandler? cancelKeyHandler = null;
            PosixSignalRegistration? sigtermRegistration = null;

            DatabaseService? dbService = null;
            HttpSyncServer? httpServer = null;
            UdpDiscoveryServer? udpServer = null;
            CrossProcessEventListener? crossProcessListener = null;
            Timer? memoryTrimTimer = null;

            try
            {
                // 单实例 Mutex 探测
                mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
                hasMutex = createdNew;

                if (!hasMutex)
                {
                    try
                    {
                        // 尝试在 0 毫秒内获取，以检测前序进程是否处于释放中或已废弃
                        hasMutex = mutex.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        // 前序进程非正常终止，操作系统已将互斥量所有权转移给当前进程
                        hasMutex = true;
                        CrashLogger.LogInfo("TampoSyncService acquired abandoned mutex from prior terminated instance.");
                    }
                }

                if (!hasMutex)
                {
                    CrashLogger.LogInfo("Another instance of TampoSyncService is already running. Exiting cleanly with code 0.");
                    return 0; // 纯净退出，退出码 0 (Mutex 在 finally 中安全销毁句柄，不调用 ReleaseMutex)
                }

                CrashLogger.LogInfo($"TampoSyncService started. PID: {Environment.ProcessId}, Version: {HttpSyncServer.ServerVersion}");

                // 3. 构建协作式停机 CancellationTokenSource 与等待同步原语
                cts = new CancellationTokenSource();
                shutdownCompleted = new ManualResetEventSlim(false);

                // 安全关机触发委托：彻底防御 ObjectDisposedException
                void RequestShutdown(string source)
                {
                    try
                    {
                        if (cts != null && !cts.IsCancellationRequested)
                        {
                            CrashLogger.LogInfo($"{source} received. Initiating graceful shutdown...");
                            cts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // CTS 已释放，表明关机清理流程已执行完毕，安全忽略
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"RequestShutdown.{source}");
                    }
                }

                // 注册 ProcessExit 事件 (含 ObjectDisposedException 防御与最多 2 秒等待主程序清理)
                processExitHandler = (s, e) =>
                {
                    RequestShutdown("ProcessExit");
                    try
                    {
                        shutdownCompleted?.Wait(TimeSpan.FromSeconds(2));
                    }
                    catch { }
                };
                AppDomain.CurrentDomain.ProcessExit += processExitHandler;

                // 注册 Console.CancelKeyPress 事件
                cancelKeyHandler = (s, e) =>
                {
                    e.Cancel = true; // 阻止 CLR 立即强制 Kill 进程
                    RequestShutdown("Console.CancelKeyPress");
                };
                Console.CancelKeyPress += cancelKeyHandler;

                // 注册 POSIX SIGTERM 信号 (Windows .NET 8 支持)
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                        {
                            context.Cancel = true;
                            RequestShutdown("SIGTERM");
                        });
                    }
                    catch { }
                }

                // 4. 初始化数据库核心服务
                dbService = new DatabaseService();
                await dbService.InitializeAsync();
                // 守护服务自身写库由 HTTP 合并结果通知客户端，不向自己的跨进程监听器重复发信号。
                DatabaseService.IsAutoSyncOnStudyEnabled = false;

                // 5. 启动网络监听服务 (HTTP 52080 + UDP 52081)
                httpServer = new HttpSyncServer(dbService, cts);
                httpServer.Start();

                udpServer = new UdpDiscoveryServer();
                udpServer.Start();

                // 5.1 启动跨进程数据变动监听器（感知主程序写库，毫秒级广播 SSE 下行事件给手机端）
                if (OperatingSystem.IsWindows())
                {
                    crossProcessListener = new CrossProcessEventListener(dbService, () =>
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                long ver = await dbService.GetDataVersionAsync();
                                if (httpServer != null)
                                {
                                    await httpServer.BroadcastDataChangedAsync(ver);
                                }
                            }
                            catch (Exception ex)
                            {
                                CrashLogger.LogException(ex, "SyncService.OnDataChangedBroadcast");
                            }
                        });
                    }, CrossProcessEventHelper.ToServiceEventName);
                    await crossProcessListener.StartAsync();
                }

                // 6. 启动 15 分钟周期性工作集修剪定时器
                memoryTrimTimer = new Timer(_ =>
                {
                    MemoryOptimizer.TrimWorkingSet();
                }, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

                // 7. 初始内存修剪 (<30MB 保障)
                MemoryOptimizer.TrimWorkingSet();
                CrashLogger.LogInfo("Initial memory working set trimmed successfully.");

                // 8. 主工作等待循环：以零 CPU 消耗挂起直到接收到关机信号
                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 预期的关机取消
                }

                // 9. 优雅关机清理序列 (严格按序执行)
                CrashLogger.LogInfo("Executing shutdown cleanup sequence...");

                // (1) 停止 UDP 发现服务（停止广播与应答，避免移动端继续发送探测）
                try
                {
                    udpServer?.Stop();
                    udpServer?.Dispose();
                    udpServer = null;
                    CrashLogger.LogInfo("UdpDiscoveryServer stopped.");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "Shutdown.UdpStop");
                }

                // (2) 停止 HTTP 同步服务（停止接收新请求，释放监听端口）
                try
                {
                    httpServer?.Stop();
                    httpServer?.Dispose();
                    httpServer = null;
                    CrashLogger.LogInfo("HttpSyncServer stopped.");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "Shutdown.HttpStop");
                }

                // (2.1) 停止跨进程事件监听器
                try
                {
                    crossProcessListener?.Dispose();
                    crossProcessListener = null;
                    CrashLogger.LogInfo("CrossProcessEventListener stopped.");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "Shutdown.CrossProcessListenerDispose");
                }

                // (3) 停止并释放周期性工作集修剪定时器
                try
                {
                    memoryTrimTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    memoryTrimTimer?.Dispose();
                    memoryTrimTimer = null;
                    CrashLogger.LogInfo("Periodic memory trim timer disposed.");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "Shutdown.MemoryTrimTimerDispose");
                }

                // (4) 安全断开 SQLite 数据库连接
                if (dbService != null)
                {
                    try
                    {
                        await dbService.CloseAsync();
                        CrashLogger.LogInfo("DatabaseService closed successfully.");
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "Shutdown.DbClose");
                    }
                }

                // (5) 重置 SQLite 连接池句柄，确保 WAL 文件与连接句柄完全解除占用
                try
                {
                    SQLiteAsyncConnection.ResetPool();
                    CrashLogger.LogInfo("SQLiteAsyncConnection connection pool reset.");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "Shutdown.ResetPool");
                }

                // 标记关机清理已全部完成，通知 ProcessExit 等待线程解除阻塞
                shutdownCompleted?.Set();

                CrashLogger.LogInfo("TampoSyncService shutdown completed cleanly with exit code 0.");
                return 0;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "TampoSyncService.Main.Fatal");

                // 异常终止时的防御性资源回收
                try { udpServer?.Stop(); udpServer?.Dispose(); } catch { }
                try { httpServer?.Stop(); httpServer?.Dispose(); } catch { }
                try { memoryTrimTimer?.Dispose(); } catch { }
                if (dbService != null)
                {
                    try { await dbService.CloseAsync(); } catch { }
                }
                try { SQLiteAsyncConnection.ResetPool(); } catch { }
                try { shutdownCompleted?.Set(); } catch { }

                return 1;
            }
            finally
            {
                // 注销系统事件与信号监听，杜绝进程退出后的二次触发与 ObjectDisposedException
                if (processExitHandler != null)
                {
                    try { AppDomain.CurrentDomain.ProcessExit -= processExitHandler; } catch { }
                }
                if (cancelKeyHandler != null)
                {
                    try { Console.CancelKeyPress -= cancelKeyHandler; } catch { }
                }
                try { sigtermRegistration?.Dispose(); } catch { }

                // 释放同步原语与 CTS
                try { shutdownCompleted?.Dispose(); } catch { }
                try { cts?.Dispose(); } catch { }

                // (6) 确保全局 Mutex 被安全释放与销毁 (仅在拥有互斥体所有权时调用 ReleaseMutex)
                if (mutex != null)
                {
                    if (hasMutex)
                    {
                        try
                        {
                            mutex.ReleaseMutex();
                        }
                        catch (ApplicationException)
                        {
                            // Mutex 在初始主线程获取，异步 await 延续在线程池线程执行时，非属主线程调用 ReleaseMutex 会抛出 ApplicationException。
                            // 进程退出或句柄 Dispose 时操作系统会自动释放该互斥体。
                        }
                        catch (Exception ex)
                        {
                            CrashLogger.LogException(ex, "ReleaseMutex");
                        }
                    }
                    try
                    {
                        mutex.Dispose();
                    }
                    catch { }
                }
            }
        }

        private static void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    CrashLogger.LogException(ex, "AppDomain.UnhandledException");
                }
                else
                {
                    CrashLogger.LogMessage($"Unhandled non-exception object: {args.ExceptionObject}", "FATAL");
                }
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                CrashLogger.LogException(args.Exception, "TaskScheduler.UnobservedTaskException");
                args.SetObserved();
            };
        }
    }
}
#endif
