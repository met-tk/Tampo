using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using NihongoVocab.Services;
using NihongoVocab.ViewModels;

namespace NihongoVocab
{
    public partial class App : Application
    {
        public static MainWindow? MainWindowInstance { get; set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue? UIThreadDispatcher { get; set; }
        private static readonly Dictionary<Type, object> Services = new();

        public App()
        {
            CrashLogger.LogInfo("App ctor start");
            SQLitePCL.Batteries_V2.Init();

            // 模块一：全局异常捕获与持久化日志
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    CrashLogger.LogException(ex, "AppDomain.UnhandledException");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                CrashLogger.LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
                e.SetObserved();
            };

            this.UnhandledException += (s, e) =>
            {
                CrashLogger.LogException(e.Exception, "Application.UnhandledException");
                e.Handled = true;
            };

            InitializeComponent();
            ConfigureServices();
        }

        private void ConfigureServices()
        {
            var dbService = new DatabaseService();
            var prefService = new UserPreferenceService(dbService);
            var presetManager = new PresetManager();
            var webExportService = new WebExportService();

            LocalizationService.LanguageLoader = () => prefService.Get("App_Language", "zh-CN");
            LocalizationService.LanguagePersister = (lang) => prefService.Set("App_Language", lang);

            Services[typeof(DatabaseService)] = dbService;
            Services[typeof(UserPreferenceService)] = prefService;
            Services[typeof(PresetManager)] = presetManager;
            Services[typeof(LocalizationService)] = LocalizationService.Instance;
            Services[typeof(WebExportService)] = webExportService;

            // ViewModels
            Services[typeof(ImportViewModel)] = new ImportViewModel(dbService, presetManager);
            Services[typeof(WordListViewModel)] = new WordListViewModel(dbService, webExportService);
            Services[typeof(AllWordsViewModel)] = new AllWordsViewModel(dbService);
            Services[typeof(StudyViewModel)] = new StudyViewModel(dbService);
            Services[typeof(TodayStudyViewModel)] = new TodayStudyViewModel(dbService);
            Services[typeof(RecentStudyViewModel)] = new RecentStudyViewModel(dbService);
            Services[typeof(StatisticsViewModel)] = new StatisticsViewModel(dbService);
            Services[typeof(ActivityCalendarViewModel)] = new ActivityCalendarViewModel(dbService);
            Services[typeof(MemoryAnalyticsViewModel)] = new MemoryAnalyticsViewModel(dbService);
            Services[typeof(SettingsViewModel)] = new SettingsViewModel();
        }

        public static T GetService<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service))
            {
                return (T)service;
            }
            throw new InvalidOperationException($"未注册的服务类型：{typeof(T).FullName}");
        }

        public static void RunOnUIThread(Action action)
        {
            if (UIThreadDispatcher != null)
            {
                UIThreadDispatcher.TryEnqueue(() => action());
            }
            else
            {
                action();
            }
        }

        private MainWindow? _mainWindow;

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            CrashLogger.LogInfo("OnLaunched entered");
            try
            {
                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    CrashLogger.LogInfo("AppDomain.ProcessExit called!");
                };

                _mainWindow = new MainWindow();
                MainWindowInstance = _mainWindow;
                UIThreadDispatcher = _mainWindow.DispatcherQueue;
                _mainWindow.Activate();
                CrashLogger.LogInfo("MainWindowInstance activated");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var db = GetService<DatabaseService>();
                        await db.InitializeAsync();
                        CrashLogger.LogInfo("DatabaseService.InitializeAsync completed");

                        var pref = GetService<UserPreferenceService>();
                        await pref.LoadAllAsync();
                        CrashLogger.LogInfo("UserPreferenceService.LoadAllAsync completed");

                        string savedLang = pref.Get("App_Language", "zh-CN");
                        if (!string.IsNullOrEmpty(savedLang) && savedLang != LocalizationService.Instance.CurrentLanguage)
                        {
                            UIThreadDispatcher?.TryEnqueue(() =>
                            {
                                LocalizationService.Instance.SetLanguage(savedLang);
                            });
                        }

                        // 数据加载完毕后，回到UI线程重新应用真实的窗口几何（因为构造时Pref尚未加载）
                        UIThreadDispatcher?.TryEnqueue(() =>
                        {
                            MainWindowInstance?.ApplyRestoredGeometry();
                        });
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, "App.BackgroundInit");
                    }
                });
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "App.OnLaunched");
                throw;
            }
        }
    }
}
