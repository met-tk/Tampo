using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using NihongoVocab.Services;

namespace NihongoVocab.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _currentLanguage;

        [ObservableProperty]
        private ElementTheme _currentTheme = ElementTheme.Default;

        [ObservableProperty]
        private string _logPath;

        public string SettingsTitle => LocalizationService.Instance.GetString("SettingsTitle", "设置");
        public string AppearanceAndLanguage => LocalizationService.Instance.GetString("AppearanceAndLanguage", "外观与语言");
        public string ThemeSettings => LocalizationService.Instance.GetString("ThemeSettings", "应用主题");
        public string ThemeDefault => LocalizationService.Instance.GetString("ThemeDefault", "跟随系统");
        public string ThemeLight => LocalizationService.Instance.GetString("ThemeLight", "明亮模式");
        public string ThemeDark => LocalizationService.Instance.GetString("ThemeDark", "深色模式");
        public string LanguageSettings => LocalizationService.Instance.GetString("LanguageSettings", "显示语言");
        public string DiagnosticAndLogs => LocalizationService.Instance.GetString("DiagnosticAndLogs", "诊断与日志");
        public string LogPathLabel => LocalizationService.Instance.GetString("LogPathLabel", "运行日志路径");
        public string ButtonOpenDirectory => LocalizationService.Instance.GetString("ButtonOpenDirectory", "打开目录");
        public string DataMaintenance => LocalizationService.Instance.GetString("DataMaintenance", "数据维护");
        public string ClearDataLabel => LocalizationService.Instance.GetString("ClearDataLabel", "清空所有词库与复习数据");
        public string ClearDataSubLabel => LocalizationService.Instance.GetString("ClearDataSubLabel", "彻底清空本地所有单词、词单及复习日志，不可恢复");
        public string ButtonClearAllData => LocalizationService.Instance.GetString("ButtonClearAllData", "清空所有数据");
        public string AboutTitle => LocalizationService.Instance.GetString("AboutTitle", "关于 Tampo");
        public string VersionLabel => LocalizationService.Instance.GetString("VersionLabel", "版本：1.1.0");
        public string ArchLabel => LocalizationService.Instance.GetString("ArchLabel", "架构：.NET 8 · WinUI 3 · SQLite");
        public string FsrsModelLabel => LocalizationService.Instance.GetString("FsrsModelLabel", "调度模型：FSRS (Free Spaced Repetition Scheduler)");

        public SettingsViewModel()
        {
            _currentLanguage = LocalizationService.Instance.CurrentLanguage;
            _logPath = CrashLogger.GetLogPath();

            string savedTheme = UserPreferenceService.Instance.Get("App_Theme", "Default");
            if (Enum.TryParse<ElementTheme>(savedTheme, out var parsedTheme))
            {
                _currentTheme = parsedTheme;
            }

            LocalizationService.Instance.LanguageChanged += (s, e) =>
            {
                OnPropertyChanged(string.Empty);
            };
        }

        public void SetLanguage(string langTag)
        {
            CurrentLanguage = langTag;
            UserPreferenceService.Instance.Set("App_Language", langTag);
            LocalizationService.Instance.SetLanguage(langTag);
        }

        public void SetTheme(ElementTheme theme)
        {
            CurrentTheme = theme;
            UserPreferenceService.Instance.Set("App_Theme", theme.ToString());
            if (App.MainWindowInstance?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
        }

        public async Task ClearAllDataAsync()
        {
            var db = App.GetService<DatabaseService>();
            await db.ClearAllDataAsync();
        }
    }
}
