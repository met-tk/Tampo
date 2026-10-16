using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 局域网同步守护服务 (TampoSyncService.exe) 管控器
    /// 负责主程序与独立后台守护进程的生命周期管理、本地 HTTP 探针通信与 Windows 开机自启配置
    /// </summary>
    public class SyncServiceController
    {
        private static SyncServiceController? _instance;
        public static SyncServiceController Instance => _instance ??= new SyncServiceController();

        public const string ServiceProcessName = "TampoSyncService";
        public const string ServiceExecutableFileName = "TampoSyncService.exe";
        public const string AutostartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string AutostartValueName = "TampoSyncService";

        public const int DefaultHttpPort = 52080;
        public const int DefaultUdpPort = 52081;

        private static readonly string PingUrl = $"http://127.0.0.1:{DefaultHttpPort}/api/ping";
        private static readonly string StopUrl = $"http://127.0.0.1:{DefaultHttpPort}/api/control/stop";

        private readonly HttpClient _httpClient;
        private string? _cachedExecutablePath;

        public event Action<bool>? ServiceStatusChanged;

        public SyncServiceController(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        /// <summary>
        /// 解析 TampoSyncService.exe 的绝对路径
        /// </summary>
        public string GetExecutablePath()
        {
            if (!string.IsNullOrEmpty(_cachedExecutablePath) && File.Exists(_cachedExecutablePath))
            {
                return _cachedExecutablePath;
            }

            string baseDir = AppContext.BaseDirectory;
            string primaryPath = Path.Combine(baseDir, ServiceExecutableFileName);
            if (File.Exists(primaryPath))
            {
                _cachedExecutablePath = primaryPath;
                return primaryPath;
            }

            // 本地开发与单元测试环境下的候选路径搜索
            string[] probePaths = new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\SyncService\bin\x64\Debug\net8.0-windows", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\SyncService\bin\Debug\net8.0-windows", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\SyncService\bin\x64\Release\net8.0-windows", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\SyncService\bin\Release\net8.0-windows", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\publish", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"SyncService\bin\Debug\net8.0-windows", ServiceExecutableFileName)),
                Path.GetFullPath(Path.Combine(baseDir, @"SyncService", ServiceExecutableFileName))
            };

            foreach (var probe in probePaths)
            {
                if (File.Exists(probe))
                {
                    _cachedExecutablePath = probe;
                    return probe;
                }
            }

            return primaryPath;
        }

        /// <summary>
        /// 判断守护服务当前是否正在运行
        /// 采用双重探测机制：优先执行 HTTP /api/ping 探针，若未响应则检测进程表中是否存在 TampoSyncService 进程
        /// </summary>
        public async Task<bool> IsServiceRunningAsync()
        {
            // 1. 优先尝试本地 HTTP 探针探测
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
                using var response = await _httpClient.GetAsync(PingUrl, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // HTTP 探针超时或未监听，进入进程回退探测
            }

            // 2. 回退检测进程实例
            try
            {
                var processes = Process.GetProcessesByName(ServiceProcessName);
                bool hasProcess = processes.Length > 0;
                foreach (var p in processes)
                {
                    p.Dispose();
                }
                return hasProcess;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.IsServiceRunningAsync.ProcessCheck");
                return false;
            }
        }

        /// <summary>
        /// 启动独立的后台守护进程
        /// </summary>
        public async Task<bool> StartServiceAsync()
        {
            try
            {
                if (await IsServiceRunningAsync())
                {
                    ServiceStatusChanged?.Invoke(true);
                    return true;
                }

                string exePath = GetExecutablePath();
                if (!File.Exists(exePath))
                {
                    CrashLogger.LogMessage($"未找到守护服务可执行文件：{exePath}", "ERROR");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    CrashLogger.LogMessage("Process.Start 返回 null", "ERROR");
                    return false;
                }

                // 轮询等待服务初始化并响应 Ping（最多等待 3000ms）
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(200);
                    if (await IsServiceRunningAsync())
                    {
                        ServiceStatusChanged?.Invoke(true);
                        return true;
                    }
                }

                CrashLogger.LogMessage("守护服务已拉起但未能在 3 秒内成功响应 Ping 探测", "ERROR");
                return await IsServiceRunningAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.StartServiceAsync");
                return false;
            }
        }

        /// <summary>
        /// 停止后台守护服务
        /// 优先向 127.0.0.1 发起 POST /api/control/stop 请求优雅退出，若超时未退出则执行进程强制终止
        /// </summary>
        public async Task<bool> StopServiceAsync()
        {
            try
            {
                // 1. 发起回环优雅停止指令
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var content = new StringContent("{}", Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync(StopUrl, content, cts.Token);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogInfo($"优雅退出指令发送失败或超时（将尝试进程强制回收）：{ex.Message}");
                }

                // 2. 轮询等待进程自然退出（最多 2 秒）
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(200);
                    var procs = Process.GetProcessesByName(ServiceProcessName);
                    bool anyAlive = procs.Length > 0;
                    foreach (var p in procs) p.Dispose();
                    if (!anyAlive)
                    {
                        ServiceStatusChanged?.Invoke(false);
                        return true;
                    }
                }

                // 3. 强制回收残留进程
                var remaining = Process.GetProcessesByName(ServiceProcessName);
                foreach (var p in remaining)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                            p.WaitForExit(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "SyncServiceController.StopServiceAsync.Kill");
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                ServiceStatusChanged?.Invoke(false);
                return true;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.StopServiceAsync");
                return false;
            }
        }

        /// <summary>
        /// 向后台同步服务设置数据维护锁定模式。
        /// 在数据恢复与清空期间锁定同步（移动端请求将返回 503 阻断），防止脏数据或旧墓碑冲刷恢复的数据。
        /// </summary>
        public async Task<bool> SetMaintenanceModeAsync(bool enabled)
        {
            try
            {
                bool isRunning = await IsServiceRunningAsync();
                if (!isRunning) return false;

                string url = $"http://127.0.0.1:{DefaultHttpPort}/api/control/maintenance-mode?enabled={(enabled ? "true" : "false")}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var response = await _httpClient.PostAsync(url, null, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"SyncServiceController.SetMaintenanceModeAsync({enabled})");
                return false;
            }
        }

        /// <summary>
        /// 查询局域网同步守护服务中已绑定设备的实时连接与在线状态
        /// </summary>
        public async Task<System.Collections.Generic.List<Models.PairedDevice>> GetConnectedDevicesStatusAsync()
        {
            var list = new System.Collections.Generic.List<Models.PairedDevice>();
            try
            {
                bool isRunning = await IsServiceRunningAsync();
                if (!isRunning) return list;

                string url = $"http://127.0.0.1:{DefaultHttpPort}/api/devices/status";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var response = await _httpClient.GetAsync(url, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("devices", out var devArr) && devArr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var el in devArr.EnumerateArray())
                        {
                            var d = new Models.PairedDevice
                            {
                                Id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                                DeviceName = el.TryGetProperty("deviceName", out var dn) ? dn.GetString() ?? "" : "",
                                DeviceModel = el.TryGetProperty("deviceModel", out var dm) ? dm.GetString() : null,
                                CustomAlias = el.TryGetProperty("customAlias", out var ca) ? ca.GetString() : null,
                                ClientIp = el.TryGetProperty("clientIp", out var ip) ? ip.GetString() : null,
                                IsOnline = el.TryGetProperty("isOnline", out var on) && on.GetBoolean(),
                                LastSeenAt = el.TryGetProperty("lastSeenAt", out var ls) ? ls.GetInt64() : 0,
                                CreatedAt = el.TryGetProperty("createdAt", out var cr) ? cr.GetInt64() : 0,
                            };
                            list.Add(d);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.GetConnectedDevicesStatusAsync");
            }
            return list;
        }

        /// <summary>
        /// 向后台同步服务发送请求，通知指定或所有已连接的手机端主动发起全量数据推送（灾难恢复/电脑端反向拉取）
        /// </summary>
        public async Task<(bool Success, int NotifiedDevices, string Message)> RequestMobilePushAsync(string? targetDeviceId = null)
        {
            try
            {
                bool isRunning = await IsServiceRunningAsync();
                if (!isRunning)
                {
                    return (false, 0, "局域网同步服务未运行，请先开启同步服务");
                }

                string url = string.IsNullOrWhiteSpace(targetDeviceId)
                    ? $"http://127.0.0.1:{DefaultHttpPort}/api/sync/request-mobile-push"
                    : $"http://127.0.0.1:{DefaultHttpPort}/api/sync/request-mobile-push?targetDeviceId={Uri.EscapeDataString(targetDeviceId.Trim())}";

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClient.PostAsync(url, null, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    int count = root.TryGetProperty("notifiedDevices", out var n) ? n.GetInt32() : 0;
                    string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                    return (true, count, msg);
                }
                return (false, 0, $"请求失败：HTTP {response.StatusCode}");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.RequestMobilePushAsync");
                return (false, 0, $"请求异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 检查当前是否已启用 Windows 开机自启动
        /// </summary>
        public bool IsAutostartEnabled()
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, false);
                if (key == null) return false;
                var value = key.GetValue(AutostartValueName);
                return value != null;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "SyncServiceController.IsAutostartEnabled");
                return false;
            }
        }

        /// <summary>
        /// 配置 Windows 开机自启状态
        /// </summary>
        /// <param name="enable">true: 写入注册表 Run 键；false: 从注册表移除</param>
        public bool SetAutostart(bool enable)
        {
            if (!OperatingSystem.IsWindows()) return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(AutostartRegistryKey, true);
                if (key == null)
                {
                    CrashLogger.LogMessage($"无法打开注册表项：{AutostartRegistryKey}", "ERROR");
                    return false;
                }

                if (enable)
                {
                    string exePath = GetExecutablePath();
                    // 严格包裹双引号，防止路径包含空格时被 Windows 解析截断
                    string command = $"\"{exePath}\"";
                    key.SetValue(AutostartValueName, command, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(AutostartValueName, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"SyncServiceController.SetAutostart({enable})");
                return false;
            }
        }
    }
}
