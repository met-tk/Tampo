using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NihongoVocab.Services;
using NihongoVocab.Views;

namespace NihongoVocab
{
    public sealed partial class MainWindow : Window
    {
        private static ContentDialog? _currentActiveDialog;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        public MainWindow()
        {
            CrashLogger.LogInfo("MainWindow ctor start");
            this.InitializeComponent();
            App.MainWindowInstance = this;
            SetWindowIcon();
            RestoreWindowGeometry();

            // 订阅语言变更事件，实现原生 ResourceManager 驱动的即时三语切换
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();

            // 恢复上次保存的明暗主题
            string savedTheme = UserPreferenceService.Instance.Get("App_Theme", "Default");
            if (Enum.TryParse<ElementTheme>(savedTheme, out var parsedTheme) && this.Content is FrameworkElement rootElem)
            {
                rootElem.RequestedTheme = parsedTheme;
            }

            this.Closed += (s, e) =>
            {
                CrashLogger.LogInfo("MainWindow.Closed event fired!");
            };

            CrashLogger.LogInfo("MainWindow ctor end");
        }

        private void RestoreWindowGeometry()
        {
            // 第一阶段：仅注册 AppWindow 事件，真正的几何恢复在数据加载后由 ApplyRestoredGeometry() 完成
            try
            {
                this.AppWindow.Changed += AppWindow_Changed;
                this.AppWindow.Closing += AppWindow_Closing;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.RestoreWindowGeometry");
            }
        }

        /// <summary>
        /// 第二阶段：在 UserPreferenceService 数据已加载完毕后，从偏好中读取并应用真实的窗口位置/尺寸。
        /// 由 App.OnLaunched 的后台任务在加载完成后通过 DispatcherQueue 回调此方法。
        /// </summary>
        public void ApplyRestoredGeometry()
        {
            try
            {
                var pref = UserPreferenceService.Instance;
                int width = pref.GetInt("Window_Width", 1200);
                int height = pref.GetInt("Window_Height", 800);
                int x = pref.GetInt("Window_X", -1);
                int y = pref.GetInt("Window_Y", -1);
                bool isMaximized = pref.GetBool("Window_IsMaximized", false);

                CrashLogger.LogInfo($"ApplyRestoredGeometry: w={width} h={height} x={x} y={y} max={isMaximized}");

                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

                if (displayArea != null)
                {
                    var workArea = displayArea.WorkArea;
                    if (width < 600 || width > workArea.Width) width = Math.Min(1200, workArea.Width);
                    if (height < 400 || height > workArea.Height) height = Math.Min(800, workArea.Height);

                    // 坐标不合法则安全居中
                    if (x < workArea.X || x > workArea.X + workArea.Width - 200 ||
                        y < workArea.Y || y > workArea.Y + workArea.Height - 100)
                    {
                        x = workArea.X + (workArea.Width - width) / 2;
                        y = workArea.Y + (workArea.Height - height) / 2;
                    }

                    this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
                }
                else
                {
                    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(width, 1000), Math.Max(height, 700)));
                }

                if (isMaximized && this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }

                CrashLogger.LogInfo("ApplyRestoredGeometry done");
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.ApplyRestoredGeometry");
            }
        }

        private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                SaveWindowGeometry();
            }
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            SaveWindowGeometry();
        }

        private void SaveWindowGeometry()
        {
            try
            {
                if (this.AppWindow == null) return;
                var pref = UserPreferenceService.Instance;

                if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    bool isMaximized = (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized);
                    pref.SetBool("Window_IsMaximized", isMaximized);

                    if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Restored)
                    {
                        var pos = this.AppWindow.Position;
                        var size = this.AppWindow.Size;
                        if (size.Width >= 400 && size.Height >= 300)
                        {
                            pref.SetInt("Window_X", pos.X);
                            pref.SetInt("Window_Y", pos.Y);
                            pref.SetInt("Window_Width", size.Width);
                            pref.SetInt("Window_Height", size.Height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.SaveWindowGeometry");
            }
        }

        private void SetWindowIcon()
        {
            try
            {
                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    this.AppWindow.SetIcon(iconPath);

                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    if (hWnd != IntPtr.Zero)
                    {
                        IntPtr hIconBig = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x00000010);
                        IntPtr hIconSmall = LoadImage(IntPtr.Zero, iconPath, 1, 16, 16, 0x00000010);
                        if (hIconBig != IntPtr.Zero)
                        {
                            SendMessage(hWnd, 0x0080, (IntPtr)1, hIconBig);
                        }
                        if (hIconSmall != IntPtr.Zero)
                        {
                            SendMessage(hWnd, 0x0080, (IntPtr)0, hIconSmall);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.SetWindowIcon");
            }
        }

        public static void RegisterActiveDialog(ContentDialog dialog)
        {
            _currentActiveDialog = dialog;
        }

        public static void UnregisterActiveDialog(ContentDialog dialog)
        {
            if (_currentActiveDialog == dialog)
            {
                _currentActiveDialog = null;
            }
        }

        public static void SafeCloseActiveDialog()
        {
            if (_currentActiveDialog != null)
            {
                try
                {
                    _currentActiveDialog.Hide();
                }
                catch
                {
                }
                _currentActiveDialog = null;
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            CrashLogger.LogInfo("NavView_Loaded triggered");
            NavView.SelectedItem = NavItemImport;
            NavigateTo("Import");
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            SafeCloseActiveDialog();

            if (args.IsSettingsSelected)
            {
                NavigateTo("Settings");
                return;
            }

            if (args.SelectedItemContainer is NavigationViewItem selectedItem && selectedItem.Tag is string tag)
            {
                NavigateTo(tag);
            }
        }

        private void NavigateTo(string tag)
        {
            CrashLogger.LogInfo($"NavigateTo({tag}) called");
            Type? pageType = tag switch
            {
                "Import" => typeof(ImportPage),
                "WordLists" => typeof(WordListPage),
                "AllWords" => typeof(AllWordsPage),
                "Study" => typeof(StudyPage),
                "TodayStudy" => typeof(TodayStudyPage),
                "RecentStudy" => typeof(RecentStudyPage),
                "Activity" => typeof(ActivityCalendarPage),
                "Analytics" => typeof(MemoryAnalyticsPage),
                "Settings" => typeof(SettingsPage),
                _ => typeof(ImportPage)
            };

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                try
                {
                    ContentFrame.Navigate(pageType,
                        null,
                        new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
                    CrashLogger.LogInfo($"NavigateTo({tag}) success");
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, $"MainWindow.NavigateTo({tag})");
                }
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateLocalizedStrings();

                // 语言切换后对当前活动页面进行整体无缝重新导航刷新（设置页自身有独立响应机制，避免重导航打断操作）
                var currentType = ContentFrame.CurrentSourcePageType;
                if (currentType != null && currentType != typeof(SettingsPage))
                {
                    try
                    {
                        ContentFrame.Navigate(currentType, null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "MainWindow.OnLanguageChanged.RefreshPage");
                    }
                }
            });
        }

        private void UpdateLocalizedStrings()
        {
            var loc = LocalizationService.Instance;

            this.Title = "Tampo";
            if (AppTitleTextBlock != null) AppTitleTextBlock.Text = "Tampo";
            if (NavTextImport != null) NavTextImport.Text = loc.GetString("NavImport", "导入");
            if (NavTextWordLists != null) NavTextWordLists.Text = loc.GetString("NavWordLists", "归档");
            if (NavTextAllWords != null) NavTextAllWords.Text = loc.GetString("NavAllWords", "词库");
            if (NavTextStudy != null) NavTextStudy.Text = loc.GetString("NavStudy", "学习");
            if (NavTextTodayStudy != null) NavTextTodayStudy.Text = loc.GetString("NavTodayStudy", "今日");
            if (NavTextRecentStudy != null) NavTextRecentStudy.Text = loc.GetString("NavRecentStudy", "最近学习");
            if (NavTextActivity != null) NavTextActivity.Text = loc.GetString("NavActivity", "日历");
            if (NavTextAnalytics != null) NavTextAnalytics.Text = loc.GetString("NavAnalytics", "记忆分析");

            if (NavView?.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.Content = loc.GetString("NavSettings", "设置");
            }
        }
    }
}
