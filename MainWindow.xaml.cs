using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NihongoVocab.Services;
using NihongoVocab.Views;

namespace NihongoVocab
{
    public sealed partial class MainWindow : Window
    {
        private static ContentDialog? _currentActiveDialog;
        private IntPtr _hIconBig = IntPtr.Zero;
        private IntPtr _hIconSmall = IntPtr.Zero;
        private SubclassProcDelegate? _subclassDelegate;
        private IntPtr _hwnd = IntPtr.Zero;
        private const uint SubclassId = 1001;
        private string _currentImmersiveContext = "Study";

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private const uint WM_GETMINMAXINFO = 0x0024;
        private const uint WM_NCDESTROY = 0x0082;
        private delegate IntPtr SubclassProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, UIntPtr uIdSubclass);

        [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public MainWindow()
        {
            CrashLogger.LogInfo("MainWindow ctor start");
            this.InitializeComponent();
            App.MainWindowInstance = this;
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            InitWindowSubclass();
            SetWindowIcon();
            RestoreWindowGeometry();

            // 订阅语言变更事件，实现原生 ResourceManager 驱动的即时三语切换
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedStrings();

            // 恢复上次保存的明暗主题
            string savedTheme = UserPreferenceService.Instance.Get("App_Theme", "Default");
            ElementTheme currentTheme = ElementTheme.Default;
            if (Enum.TryParse<ElementTheme>(savedTheme, out var parsedTheme))
            {
                currentTheme = parsedTheme;
            }

            if (this.Content is FrameworkElement rootElem)
            {
                rootElem.RequestedTheme = currentTheme;
                rootElem.ActualThemeChanged += (s, e) =>
                {
                    UpdateTitleBarTheme(rootElem.RequestedTheme);
                };
            }
            UpdateTitleBarTheme(currentTheme);

            this.Closed += (s, e) =>
            {
                LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                ReleaseWindowSubclass();
                ReleaseNativeIcons();
                CrashLogger.LogInfo("MainWindow.Closed event fired!");
            };

            CrashLogger.LogInfo("MainWindow ctor end");
        }

        private void ReleaseNativeIcons()
        {
            if (_hIconBig != IntPtr.Zero)
            {
                DestroyIcon(_hIconBig);
                _hIconBig = IntPtr.Zero;
            }
            if (_hIconSmall != IntPtr.Zero)
            {
                DestroyIcon(_hIconSmall);
                _hIconSmall = IntPtr.Zero;
            }
        }

private void InitWindowSubclass()
        {
            try
            {
                if (_hwnd != IntPtr.Zero)
                {
                    _subclassDelegate = new SubclassProcDelegate(WindowSubclassProc);
                    SetWindowSubclass(_hwnd, _subclassDelegate, (UIntPtr)SubclassId, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.InitWindowSubclass");
            }
        }

        private void ReleaseWindowSubclass()
        {
            try
            {
                if (_hwnd != IntPtr.Zero && _subclassDelegate != null)
                {
                    RemoveWindowSubclass(_hwnd, _subclassDelegate, (UIntPtr)SubclassId);
                    _subclassDelegate = null;
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.ReleaseWindowSubclass");
            }
        }

        private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_GETMINMAXINFO)
            {
                IntPtr ret = DefSubclassProc(hWnd, uMsg, wParam, lParam);
                try
                {
                    var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    uint dpi = GetDpiForWindow(hWnd);
                    float scale = (dpi > 0) ? (dpi / 96f) : 1.0f;

                    int minW = IsImmersiveMode ? 320 : 760;
                    int minH = IsImmersiveMode ? 400 : 520;

                    mmi.ptMinTrackSize.X = (int)Math.Round(minW * scale);
                    mmi.ptMinTrackSize.Y = (int)Math.Round(minH * scale);
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
                    return IntPtr.Zero;
                }
                catch { }
                return ret;
            }

            if (uMsg == WM_NCDESTROY)
            {
                if (_subclassDelegate != null) RemoveWindowSubclass(hWnd, _subclassDelegate, uIdSubclass);
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        /// <summary>
        /// 联动 WinUI 3 窗口标题栏颜色，跟随暗色/浅色模式切换
        /// </summary>
        public void UpdateTitleBarTheme(ElementTheme theme)
        {
            try
            {
                if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                {
                    var titleBar = this.AppWindow.TitleBar;
                    if (titleBar == null) return;

                    bool isDark = false;
                    if (theme == ElementTheme.Dark)
                    {
                        isDark = true;
                    }
                    else if (theme == ElementTheme.Light)
                    {
                        isDark = false;
                    }
                    else
                    {
                        if (this.Content is FrameworkElement rootElem)
                        {
                            isDark = rootElem.ActualTheme == ElementTheme.Dark;
                        }
                        else
                        {
                            isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                        }
                    }

                    if (isDark)
                    {
                        var darkBg = Windows.UI.Color.FromArgb(255, 32, 32, 32);
                        titleBar.BackgroundColor = darkBg;
                        titleBar.ForegroundColor = Microsoft.UI.Colors.White;
                        titleBar.InactiveBackgroundColor = darkBg;
                        titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 160, 160, 160);

                        titleBar.ButtonBackgroundColor = darkBg;
                        titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 54, 54, 54);
                        titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 75, 75, 75);
                        titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                        titleBar.ButtonInactiveBackgroundColor = darkBg;
                        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 140, 140, 140);
                    }
                    else
                    {
                        var lightBg = Windows.UI.Color.FromArgb(255, 243, 243, 243);
                        titleBar.BackgroundColor = lightBg;
                        titleBar.ForegroundColor = Microsoft.UI.Colors.Black;
                        titleBar.InactiveBackgroundColor = lightBg;
                        titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);

                        titleBar.ButtonBackgroundColor = lightBg;
                        titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;
                        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 230, 230, 230);
                        titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
                        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 210, 210, 210);
                        titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Black;
                        titleBar.ButtonInactiveBackgroundColor = lightBg;
                        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 160, 160, 160);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.UpdateTitleBarTheme");
            }
        }

        private void RestoreWindowGeometry()
        {
            // 零闪烁（Zero-Jitter）：在窗口激活前同步应用保存的常规几何或屏幕工作区自适应比例
            try
            {
                WindowPlacementService.ApplyNormalPlacement(this.AppWindow);
                this.AppWindow.Closing += AppWindow_Closing;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.RestoreWindowGeometry");
            }
        }

        /// <summary>
        /// 重新应用常规模式窗口几何
        /// </summary>
        public void ApplyRestoredGeometry()
        {
            try
            {
                WindowPlacementService.ApplyNormalPlacement(this.AppWindow);
            }
            catch { }
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
                if (IsImmersiveMode)
                {
                    // 沉浸模式下独立记忆对应模块（学习或导入）小窗位置与尺寸，绝不污染常规主界面状态
                    WindowPlacementService.SaveImmersivePlacement(this.AppWindow, _currentImmersiveContext);
                }
                else
                {
                    // 常规模式下独立保存主窗口几何
                    WindowPlacementService.SaveNormalPlacement(this.AppWindow);
                    SyncToUserPreferences();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "MainWindow.SaveWindowGeometry");
            }
        }

        private void SyncToUserPreferences()
        {
            try
            {
                var pref = UserPreferenceService.Instance;
                if (pref == null || this.AppWindow == null) return;

                if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    pref.SetBool("Window_IsMaximized", presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized);
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
            catch { }
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
                        ReleaseNativeIcons();
                        _hIconBig = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x00000010);
                        _hIconSmall = LoadImage(IntPtr.Zero, iconPath, 1, 16, 16, 0x00000010);
                        if (_hIconBig != IntPtr.Zero)
                        {
                            SendMessage(hWnd, 0x0080, (IntPtr)1, _hIconBig);
                        }
                        if (_hIconSmall != IntPtr.Zero)
                        {
                            SendMessage(hWnd, 0x0080, (IntPtr)0, _hIconSmall);
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
            if (dialog != null)
            {
                DialogHelper.ApplyTheme(dialog);
            }
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

            // 恢复上次保存的左侧菜单栏宽度偏好 (160px ~ 360px)
            double savedWidth = UserPreferenceService.Instance.GetDouble("NavPane_Width", 200);
            savedWidth = Math.Clamp(savedWidth, 160, 360);
            NavView.OpenPaneLength = savedWidth;
            UpdateNavPaneSplitter();

            NavView.PaneOpened += (s, args) => UpdateNavPaneSplitter();
            NavView.PaneClosed += (s, args) => UpdateNavPaneSplitter();

            NavView.SelectedItem = NavItemImport;
            NavigateTo("Import");
        }

        #region 左侧菜单栏隐形分割滑块拖拽 (160px ~ 360px)

        private static readonly InputSystemCursor _sizeWestEastCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        private bool _isNavPaneResizing = false;
        private double _navPaneStartX;
        private double _navPaneStartWidth;

        private static readonly System.Reflection.PropertyInfo? _protectedCursorProp =
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private void SetSplitterCursor(InputCursor? cursor)
        {
            try
            {
                if (NavPaneSplitter != null)
                {
                    _protectedCursorProp?.SetValue(NavPaneSplitter, cursor);
                }
            }
            catch { }
        }

        private void NavPaneSplitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            SetSplitterCursor(_sizeWestEastCursor);
            if (NavPaneSplitterBar != null) NavPaneSplitterBar.Opacity = 0.5;
        }

        private void NavPaneSplitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isNavPaneResizing)
            {
                SetSplitterCursor(null);
                if (NavPaneSplitterBar != null) NavPaneSplitterBar.Opacity = 0;
            }
        }

        private void NavPaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isNavPaneResizing = true;
            _navPaneStartX = e.GetCurrentPoint(RootLayoutGrid).Position.X;
            _navPaneStartWidth = NavView.OpenPaneLength;
            if (NavPaneSplitterBar != null) NavPaneSplitterBar.Opacity = 1.0;
            NavPaneSplitter.CapturePointer(e.Pointer);
        }

        private void NavPaneSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isNavPaneResizing) return;

            double currentX = e.GetCurrentPoint(RootLayoutGrid).Position.X;
            double delta = currentX - _navPaneStartX;
            double targetWidth = Math.Clamp(_navPaneStartWidth + delta, 160, 360);
            NavView.OpenPaneLength = targetWidth;
            UpdateNavPaneSplitter();
        }

        private void NavPaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isNavPaneResizing)
            {
                _isNavPaneResizing = false;
                NavPaneSplitter.ReleasePointerCapture(e.Pointer);
                SetSplitterCursor(null);
                if (NavPaneSplitterBar != null) NavPaneSplitterBar.Opacity = 0;
                UserPreferenceService.Instance.SetDouble("NavPane_Width", NavView.OpenPaneLength);
            }
        }

        private void NavPaneSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isNavPaneResizing)
            {
                _isNavPaneResizing = false;
                SetSplitterCursor(null);
                if (NavPaneSplitterBar != null) NavPaneSplitterBar.Opacity = 0;
                UserPreferenceService.Instance.SetDouble("NavPane_Width", NavView.OpenPaneLength);
            }
        }

        public void UpdateNavPaneSplitter()
        {
            if (NavPaneSplitter == null) return;

            bool visible = NavView.IsPaneVisible && NavView.IsPaneOpen;
            NavPaneSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
                NavPaneSplitter.Margin = new Thickness(NavView.OpenPaneLength - 5, 0, 0, 0);
            }
        }

        #endregion

        #region 沉浸式模式与窗口置顶管控

        public bool IsAlwaysOnTop
        {
            get
            {
                if (this.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    return presenter.IsAlwaysOnTop;
                }
                return false;
            }
        }

        public void ToggleAlwaysOnTop()
        {
            SetAlwaysOnTop(!IsAlwaysOnTop);
        }

        public void SetAlwaysOnTop(bool alwaysOnTop)
        {
            if (this.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = alwaysOnTop;
            }
        }

        public bool IsImmersiveMode { get; private set; }

        public void SetImmersiveMode(bool enable, string context = "Study")
        {
            string targetContext = string.IsNullOrWhiteSpace(context) ? "Study" : context;

            if (enable)
            {
                NavView.IsPaneVisible = false;
                UpdateNavPaneSplitter();

                if (IsImmersiveMode)
                {
                    // 已在沉浸模式下切换子模块，仅需保存前模块沉浸尺寸并还原目标模块
                    if (!string.Equals(_currentImmersiveContext, targetContext, StringComparison.OrdinalIgnoreCase))
                    {
                        WindowPlacementService.SaveImmersivePlacement(this.AppWindow, _currentImmersiveContext);
                        _currentImmersiveContext = targetContext;
                        WindowPlacementService.ApplyImmersivePlacement(this.AppWindow, _currentImmersiveContext);
                    }
                    return;
                }

                _currentImmersiveContext = targetContext;
                WindowPlacementService.SaveNormalPlacement(this.AppWindow);
                IsImmersiveMode = true;
                WindowPlacementService.ApplyImmersivePlacement(this.AppWindow, _currentImmersiveContext);
            }
            else
            {
                if (!IsImmersiveMode) return; // 幂等拦截，防止大窗覆盖小窗记忆

                NavView.IsPaneVisible = true;
                UpdateNavPaneSplitter();
                WindowPlacementService.SaveImmersivePlacement(this.AppWindow, _currentImmersiveContext);
                IsImmersiveMode = false;
                WindowPlacementService.ApplyNormalPlacement(this.AppWindow);
            }
        }

        public void SetImmersiveMode(bool enable, bool compactWindow)
        {
            SetImmersiveMode(enable, compactWindow ? "Import" : "Study");
        }

        #endregion

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
            if (IsImmersiveMode)
            {
                SetImmersiveMode(false);
                SetAlwaysOnTop(false);
            }

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
