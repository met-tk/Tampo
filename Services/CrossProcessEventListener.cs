using System;
using System.Threading;
using System.Threading.Tasks;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 主程序后台监听器：监听跨进程事件与 data_version 变动，并安全调度到 UI 线程刷新
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public sealed class CrossProcessEventListener : IDisposable
    {
        private readonly DatabaseService _dbService;
        private readonly Action _onDataChangedAction;
        private readonly string _eventName;
        private readonly CancellationTokenSource _cts = new();

        private Thread? _listenerThread;
        private EventWaitHandle? _waitHandle;
        private long _lastKnownDataVersion = -1;
        private int _isChecking = 0;
        private int _hasPendingCheck = 0;
        private int _disposed = 0;

        /// <summary>
        /// 构造监听器
        /// </summary>
        /// <param name="dbService">数据库服务引用</param>
        /// <param name="onDataChangedAction">变动发生时触发的回调（若未提供，默认调用 DatabaseService.NotifyDataChanged）</param>
        /// <param name="eventName">自定义事件名称，若为 null 则使用默认 EventName</param>
        public CrossProcessEventListener(DatabaseService dbService, Action? onDataChangedAction = null, string? eventName = null)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            _onDataChangedAction = onDataChangedAction ?? (() => DatabaseService.NotifyDataChanged());
            _eventName = eventName ?? CrossProcessEventHelper.EventName;
        }

        /// <summary>
        /// 启动后台监听线程
        /// </summary>
        public void Start()
        {
            if (_disposed != 0 || _listenerThread != null) return;

            // 获取初始 data_version 基准
            Task.Run(async () =>
            {
                try
                {
                    _lastKnownDataVersion = await _dbService.GetDataVersionAsync();
                }
                catch { }
            });

            _waitHandle = CrossProcessEventHelper.TryCreateOrOpenEvent(_eventName);

            // 采用独立后台 MTA 线程，零影响线程池工作负载
            _listenerThread = new Thread(ListenLoop)
            {
                Name = "Tampo_CrossProcessEventListener_Thread",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _listenerThread.SetApartmentState(ApartmentState.MTA);
            _listenerThread.Start();
        }

        private void ListenLoop()
        {
            var cancelHandle = _cts.Token.WaitHandle;
            WaitHandle[]? handles = _waitHandle != null ? new[] { _waitHandle, cancelHandle } : null;

            while (!_cts.IsCancellationRequested && _disposed == 0)
            {
                try
                {
                    if (handles != null)
                    {
                        // 等待 Win32 命名事件（索引 0）或 取消句柄（索引 1），超时为 3000ms（Channel B 兜底）
                        int index = WaitHandle.WaitAny(handles, 3000);

                        if (index == 1)
                        {
                            // 收到取消信号，优雅退出
                            break;
                        }

                        // index == 0 (收到事件推送) 或 index == WaitHandle.WaitTimeout (3秒超时保底)
                        _ = CheckDataVersionAndDispatchAsync(isPushEvent: index == 0);
                    }
                    else
                    {
                        // 若由于权限等极端原因事件句柄未打开，回退至纯定时器轮询
                        if (cancelHandle.WaitOne(3000))
                        {
                            break;
                        }
                        _ = CheckDataVersionAndDispatchAsync(isPushEvent: false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消请求，干净退出
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // 句柄或 CTS 已释放，正在安全关闭，静默退出
                    break;
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "CrossProcessEventListener.ListenLoop");
                    Thread.Sleep(1000); // 异常退避
                }
            }
        }

        /// <summary>
        /// 异步校验数据版本并分发 UI 刷新（支持微批防抖与 Pending 标记）
        /// </summary>
        private async Task CheckDataVersionAndDispatchAsync(bool isPushEvent)
        {
            if (_disposed != 0 || _cts.IsCancellationRequested)
            {
                return;
            }

            // 标记当前有待检查的版本请求
            Interlocked.Exchange(ref _hasPendingCheck, 1);

            // 防重入控制：若已有任务正在执行循环，直接返回（当前执行中的任务会感知 _hasPendingCheck 并清空重查）
            if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
            {
                return;
            }

            try
            {
                bool delayNeeded = isPushEvent;

                while (!_cts.IsCancellationRequested && _disposed == 0)
                {
                    // 重置待处理标记，准备执行本批次检查
                    Interlocked.Exchange(ref _hasPendingCheck, 0);

                    // 事件推送模式下引入 50ms 极短微批防抖，使连续数条打卡合并为一次 UI 刷新
                    if (delayNeeded)
                    {
                        await Task.Delay(50, _cts.Token);
                    }

                    long currentVersion = await _dbService.GetDataVersionAsync();

                    if (_lastKnownDataVersion < 0)
                    {
                        _lastKnownDataVersion = currentVersion;
                    }
                    else if (currentVersion != _lastKnownDataVersion)
                    {
                        _lastKnownDataVersion = currentVersion;

                        // 安全派发
                        _onDataChangedAction?.Invoke();
                    }

                    // 后续微批次无需重复 50ms 延迟
                    delayNeeded = false;

                    // 如果在此期间没有新的检查请求，退出循环
                    if (Volatile.Read(ref _hasPendingCheck) == 0)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，静默退出
            }
            catch (ObjectDisposedException)
            {
                // 释放退出，静默退出
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "CrossProcessEventListener.CheckDataVersionAndDispatchAsync");
            }
            finally
            {
                Interlocked.Exchange(ref _isChecking, 0);

                // 边缘竞态检测：如果在跳出循环到重置 _isChecking 瞬间到达了新信号，重新拉起一次安全检查
                if (Interlocked.Exchange(ref _hasPendingCheck, 0) == 1 && _disposed == 0 && !_cts.IsCancellationRequested)
                {
                    if (Interlocked.CompareExchange(ref _isChecking, 1, 0) == 0)
                    {
                        _ = CheckDataVersionAndDispatchAsync(isPushEvent: false);
                    }
                }
            }
        }

        /// <summary>
        /// 记录本地写事务后的最新版本，防止本进程自身触发误报刷新
        /// </summary>
        public void UpdateLocalVersion(long newVersion)
        {
            _lastKnownDataVersion = newVersion;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // 1. 发送取消信号，唤醒阻塞在 WaitAny/WaitOne 或 Task.Delay 上的工作线程
            try
            {
                _cts.Cancel();
            }
            catch { }

            // 2. 宽限等待监听线程安全自然退出（至多 500ms），避免句柄在读取时被物理销毁
            try
            {
                if (_listenerThread != null && _listenerThread != Thread.CurrentThread && _listenerThread.IsAlive)
                {
                    _listenerThread.Join(500);
                }
            }
            catch { }

            // 3. 释放 OS 命名事件句柄
            try
            {
                _waitHandle?.Dispose();
            }
            catch { }

            // 4. 释放取消令牌源
            try
            {
                _cts.Dispose();
            }
            catch { }
        }
    }
}
