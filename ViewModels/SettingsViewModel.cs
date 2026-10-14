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
        private int _dayRolloverHour = 4;

        [ObservableProperty]
        private string _logPath;

        public SettingsViewModel()
        {
            _currentLanguage = LocalizationService.Instance.CurrentLanguage;
            _logPath = CrashLogger.GetLogPath();

            string savedTheme = UserPreferenceService.Instance.Get("App_Theme", "Default");
            if (Enum.TryParse<ElementTheme>(savedTheme, out var parsedTheme))
            {
                _currentTheme = parsedTheme;
            }

            _dayRolloverHour = Math.Clamp(UserPreferenceService.Instance.GetInt("App_DayRolloverHour", 4), 0, 6);
            FsrsEngine.DayRolloverHour = _dayRolloverHour;

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
            App.MainWindowInstance?.UpdateTitleBarTheme(theme);
        }

        public void SetDayRolloverHour(int hour)
        {
            int clamped = Math.Clamp(hour, 0, 6);
            DayRolloverHour = clamped;
            FsrsEngine.DayRolloverHour = clamped;
            UserPreferenceService.Instance.SetInt("App_DayRolloverHour", clamped);
            DatabaseService.NotifyDataChanged();
        }

        public async Task ClearAllDataAsync()
        {
            var db = App.GetService<DatabaseService>();
            await db.ClearAllDataAsync();
        }
    }
}
