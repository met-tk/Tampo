#if TAMPO_SYNC_SERVICE
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NihongoVocab.SyncService
{
    public static class MemoryOptimizer
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        /// <summary>
        /// 执行深度垃圾回收并强制修剪 Windows 进程工作集至最小物理开销 (<30MB 刚性约束)
        /// </summary>
        public static void TrimWorkingSet()
        {
            try
            {
                // 1. 深度收集各代垃圾对象并整理堆碎片
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                // 2. 仅在 Windows 系统下调用底层 Win32 API 释放物理工作集
                if (OperatingSystem.IsWindows())
                {
                    using var currentProcess = Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(currentProcess.Handle, (IntPtr)(-1), (IntPtr)(-1));
                }
            }
            catch
            {
                // 内存修剪失败时不阻断业务主流程
            }
        }
    }
}
#endif
