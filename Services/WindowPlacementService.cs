using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace NihongoVocab.Services
{
    public class WindowPlacementEntry
    {
        public int X { get; set; } = -1;
        public int Y { get; set; } = -1;
        public int Width { get; set; } = -1;
        public int Height { get; set; } = -1;
        public bool IsMaximized { get; set; } = false;
        public bool IsAlwaysOnTop { get; set; } = false;
        public string? DisplayId { get; set; }

        public bool IsValid => Width >= 200 && Height >= 200 && X != -1 && Y != -1;
    }

    public class WindowPlacementConfig
    {
        public WindowPlacementEntry Normal { get; set; } = new WindowPlacementEntry();
        public WindowPlacementEntry StudyImmersive { get; set; } = new WindowPlacementEntry();
        public WindowPlacementEntry ImportImmersive { get; set; } = new WindowPlacementEntry();

        // 兼容单沉浸配置的历史键名
        public WindowPlacementEntry Immersive
        {
            get => ImportImmersive;
            set => ImportImmersive = value ?? new WindowPlacementEntry();
        }

        public WindowPlacementEntry GetImmersiveEntry(string? context)
        {
            if (string.Equals(context, "Study", StringComparison.OrdinalIgnoreCase))
            {
                return StudyImmersive;
            }
            return ImportImmersive;
        }
    }

    /// <summary>
    /// 提供毫秒级同步加载的窗口布局与几何管理服务。
    /// 支持双模态（常规大屏 vs 沉浸紧凑小窗）及不同业务模块（学习模块 vs 导入模块）独立记忆、
    /// 多屏断开/拓扑感知、动态 DPI 自适应与零闪烁（Zero-Jitter）呈现。
    /// </summary>
    public static class WindowPlacementService
    {
        private static readonly object _fileLock = new object();
        private static WindowPlacementConfig? _cachedConfig;
        private static string? _configFilePath;

        public static string ConfigFilePath
        {
            get
            {
                if (_configFilePath == null)
                {
                    string appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NihongoVocab");

                    if (!Directory.Exists(appDataDir))
                    {
                        try { Directory.CreateDirectory(appDataDir); } catch { }
                    }

                    _configFilePath = Path.Combine(appDataDir, "window_placement.json");
                }
                return _configFilePath;
            }
            internal set => _configFilePath = value;
        }

        public static WindowPlacementConfig LoadConfig()
        {
            lock (_fileLock)
            {
                if (_cachedConfig != null) return _cachedConfig;

                try
                {
                    if (File.Exists(ConfigFilePath))
                    {
                        string json = File.ReadAllText(ConfigFilePath);
                        var loaded = JsonSerializer.Deserialize<WindowPlacementConfig>(json);
                        if (loaded != null)
                        {
                            _cachedConfig = loaded;
                            return _cachedConfig;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "WindowPlacementService.LoadConfig");
                }

                _cachedConfig = new WindowPlacementConfig();
                return _cachedConfig;
            }
        }

        public static void SaveConfig()
        {
            lock (_fileLock)
            {
                if (_cachedConfig == null) return;
                try
                {
                    string json = JsonSerializer.Serialize(_cachedConfig, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = ConfigFilePath + ".tmp_" + Guid.NewGuid().ToString("N");
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, ConfigFilePath, overwrite: true);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "WindowPlacementService.SaveConfig");
                }
            }
        }

        /// <summary>
        /// 应用常规模式几何属性（在窗口激活前同步调用以实现零闪烁）
        /// </summary>
        public static void ApplyNormalPlacement(AppWindow appWindow)
        {
            if (appWindow == null) return;

            try
            {
                var config = LoadConfig();
                var normal = config.Normal;

                // 若配置中尚无有效尺寸，尝试兼容从 UserPreferenceService 旧数据迁移
                if (!normal.IsValid)
                {
                    TryMigrateLegacyPreferences(normal);
                }

                // 1. 尝试匹配目标显示器（多屏幕感知）
                DisplayArea? targetDisplay = FindBestDisplayArea(normal);
                targetDisplay ??= DisplayArea.Primary;

                if (targetDisplay == null)
                {
                    appWindow.Resize(new SizeInt32(1150, 750));
                    return;
                }

                var workArea = targetDisplay.WorkArea;

                // 2. 计算常规模式最佳尺寸
                int width = normal.Width;
                int height = normal.Height;

                if (width < 600 || height < 400)
                {
                    // 首次启动或配置非法：采用工作区黄金自适应比例 (宽约 72%，高约 78%)
                    width = Math.Clamp((int)(workArea.Width * 0.72), 860, 1440);
                    height = Math.Clamp((int)(workArea.Height * 0.78), 580, 920);

                    // 确保不会撑破小屏幕（四周至少留 4% 边距）
                    width = Math.Min(width, (int)(workArea.Width * 0.94));
                    height = Math.Min(height, (int)(workArea.Height * 0.94));
                }
                else
                {
                    // 确保已有尺寸在当前工作区内不超界
                    width = Math.Clamp(width, 600, workArea.Width);
                    height = Math.Clamp(height, 400, workArea.Height);
                }

                // 4. 计算坐标并防越界
                int x = normal.X;
                int y = normal.Y;

                bool isPositionInvalid = (x == -1 || y == -1 ||
                    x < workArea.X - width + 120 ||
                    x > workArea.X + workArea.Width - 120 ||
                    y < workArea.Y ||
                    y > workArea.Y + workArea.Height - 80);

                if (isPositionInvalid)
                {
                    // 居中于目标显示器
                    x = workArea.X + (workArea.Width - width) / 2;
                    y = workArea.Y + (workArea.Height - height) / 2;
                }

                appWindow.MoveAndResize(new RectInt32(x, y, width, height));
                if (normal.IsMaximized && appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    overlapped.Maximize();
                }
                CrashLogger.LogInfo($"WindowPlacementService: Normal applied [x={x}, y={y}, w={width}, h={height}]");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WindowPlacementService.ApplyNormalPlacement");
            }
        }

        /// <summary>
        /// 应用沉浸紧凑模式几何属性（小窗、悬浮记词贴）
        /// 支持根据上下文（"Study" 学习复习 或 "Import" 快速摘抄）分别还原独立的尺寸与位置
        /// </summary>
        public static void ApplyImmersivePlacement(AppWindow appWindow, string context = "Study")
        {
            if (appWindow == null) return;

            try
            {
                var config = LoadConfig();
                var immersive = config.GetImmersiveEntry(context);

                DisplayArea? targetDisplay = FindBestDisplayArea(immersive);
                targetDisplay ??= DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
                targetDisplay ??= DisplayArea.Primary;

                if (targetDisplay == null)
                {
                    appWindow.Resize(new SizeInt32(480, 620));
                    return;
                }

                var workArea = targetDisplay.WorkArea;

                int width = immersive.Width;
                int height = immersive.Height;

                bool isStudy = string.Equals(context, "Study", StringComparison.OrdinalIgnoreCase);

                // 若尚未保存过沉浸窗口大小，提供针对性紧凑默认值
                if (width < 300 || height < 350)
                {
                    if (isStudy)
                    {
                        // 学习背词模式：稍宽一点便于卡片排版 (逻辑约 440x620)
                        width = Math.Clamp((int)(workArea.Width * 0.30), 400, 540);
                        height = Math.Clamp((int)(workArea.Height * 0.72), 520, 700);
                    }
                    else
                    {
                        // 导入摘抄模式：极致轻量便签 (逻辑约 420x580)
                        width = Math.Clamp((int)(workArea.Width * 0.28), 380, 500);
                        height = Math.Clamp((int)(workArea.Height * 0.68), 480, 660);
                    }
                }
                else
                {
                    width = Math.Clamp(width, 300, workArea.Width);
                    height = Math.Clamp(height, 350, workArea.Height);
                }

                int x = immersive.X;
                int y = immersive.Y;

                bool isPositionInvalid = (x == -1 || y == -1 ||
                    x < workArea.X - width + 80 ||
                    x > workArea.X + workArea.Width - 80 ||
                    y < workArea.Y ||
                    y > workArea.Y + workArea.Height - 60);

                if (isPositionInvalid)
                {
                    // 默认紧凑小窗自适应靠右对齐（距右边距 40px），方便与视频/网页并排
                    x = workArea.X + workArea.Width - width - 40;
                    y = workArea.Y + (workArea.Height - height) / 2;
                }

                // 沉浸模式先确保退出最大化
                if (appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Maximized)
                {
                    presenter.Restore();
                }

                appWindow.MoveAndResize(new RectInt32(x, y, width, height));
                if (appWindow.Presenter is OverlappedPresenter immersiveOverlapped)
                {
                    immersiveOverlapped.IsAlwaysOnTop = immersive.IsAlwaysOnTop;
                }
                CrashLogger.LogInfo($"WindowPlacementService: Immersive ({context}) applied [x={x}, y={y}, w={width}, h={height}]");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"WindowPlacementService.ApplyImmersivePlacement({context})");
            }
        }

        /// <summary>
        /// 记录常规模式窗口位置与大小
        /// </summary>
        public static void SaveNormalPlacement(AppWindow appWindow)
        {
            if (appWindow == null) return;

            try
            {
                var config = LoadConfig();
                var normal = config.Normal;

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    normal.IsMaximized = (presenter.State == OverlappedPresenterState.Maximized);

                    if (presenter.State == OverlappedPresenterState.Restored)
                    {
                        var pos = appWindow.Position;
                        var size = appWindow.Size;
                        if (size.Width >= 400 && size.Height >= 300)
                        {
                            normal.X = pos.X;
                            normal.Y = pos.Y;
                            normal.Width = size.Width;
                            normal.Height = size.Height;
                        }
                    }
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "WindowPlacementService.SaveNormalPlacement");
            }
        }

        /// <summary>
        /// 记录沉浸模式窗口位置与大小（支持区分 Study 或 Import 模块）
        /// </summary>
        public static void SaveImmersivePlacement(AppWindow appWindow, string context = "Study")
        {
            if (appWindow == null) return;

            try
            {
                var config = LoadConfig();
                var immersive = config.GetImmersiveEntry(context);

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    immersive.IsAlwaysOnTop = presenter.IsAlwaysOnTop;

                    if (presenter.State == OverlappedPresenterState.Restored)
                    {
                        var pos = appWindow.Position;
                        var size = appWindow.Size;
                        if (size.Width >= 280 && size.Height >= 320)
                        {
                            immersive.X = pos.X;
                            immersive.Y = pos.Y;
                            immersive.Width = size.Width;
                            immersive.Height = size.Height;
                        }
                    }
                }

                SaveConfig();
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, $"WindowPlacementService.SaveImmersivePlacement({context})");
            }
        }

        /// <summary>
        /// 多屏幕感知拓扑查找算法
        /// </summary>
        private static DisplayArea? FindBestDisplayArea(WindowPlacementEntry entry)
        {
            if (!entry.IsValid) return null;

            try
            {
                var rect = new RectInt32(entry.X, entry.Y, entry.Width, entry.Height);
                var matched = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
                if (matched != null)
                {
                    return matched;
                }
            }
            catch { }

            return null;
        }

        private static void TryMigrateLegacyPreferences(WindowPlacementEntry normal)
        {
            try
            {
                var pref = UserPreferenceService.Instance;
                if (pref != null)
                {
                    int w = pref.GetInt("Window_Width", -1);
                    int h = pref.GetInt("Window_Height", -1);
                    int x = pref.GetInt("Window_X", -1);
                    int y = pref.GetInt("Window_Y", -1);
                    bool max = pref.GetBool("Window_IsMaximized", false);

                    if (w >= 400 && h >= 300)
                    {
                        normal.Width = w;
                        normal.Height = h;
                        normal.X = x;
                        normal.Y = y;
                        normal.IsMaximized = max;
                    }
                }
            }
            catch { }
        }
    }
}
