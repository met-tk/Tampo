using System;
using System.Threading;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 提供 Windows 跨进程命名事件的安全触发与句柄获取功能
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class CrossProcessEventHelper
    {
        public const string EventName = @"Local\Tampo_DataChanged_Event_2026";
        public const string ToAppEventName = @"Local\Tampo_DataChanged_ToApp_2026";
        public const string ToServiceEventName = @"Local\Tampo_DataChanged_ToService_2026";

        /// <summary>
        /// 安全地创建或打开已存在的 Win32 跨进程命名事件句柄 (AutoReset)
        /// </summary>
        /// <param name="eventName">自定义事件名称，若为 null 则使用默认 EventName</param>
        /// <returns>成功返回 EventWaitHandle 实例；若系统权限受限等异常则返回 null</returns>
        public static EventWaitHandle? TryCreateOrOpenEvent(string? eventName = null)
        {
            string targetName = eventName ?? EventName;
            try
            {
                // 1. 优先尝试直接打开已存在的命名事件
                if (EventWaitHandle.TryOpenExisting(targetName, out var existingHandle))
                {
                    return existingHandle;
                }

                // 2. 若不存在，则尝试创建新事件 (初始无信号，自动复位)
                return new EventWaitHandle(
                    initialState: false,
                    mode: EventResetMode.AutoReset,
                    name: targetName,
                    createdNew: out _);
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(targetName, out var handle))
                    {
                        return handle;
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "CrossProcessEventHelper.TryCreateOrOpenEvent (retry failed)");
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "CrossProcessEventHelper.TryCreateOrOpenEvent");
            }

            return null;
        }

        /// <summary>
        /// 触发跨进程数据变动通知 (Set)
        /// </summary>
        /// <param name="eventName">自定义事件名称，若为 null 则使用默认 EventName</param>
        /// <returns>是否成功发送信号</returns>
        public static bool Signal(string? eventName = null)
        {
            string targetName = eventName ?? EventName;
            try
            {
                if (EventWaitHandle.TryOpenExisting(targetName, out var handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                    return true;
                }
                else
                {
                    using var newHandle = new EventWaitHandle(
                        initialState: true,
                        mode: EventResetMode.AutoReset,
                        name: targetName,
                        createdNew: out bool created);

                    if (!created)
                    {
                        newHandle.Set();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "CrossProcessEventHelper.Signal");
                return false;
            }
        }
    }
}
