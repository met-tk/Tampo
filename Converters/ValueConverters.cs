using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Windows.UI;

namespace NihongoVocab.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility v && v != Visibility.Visible;
        }
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
            {
                return 0.55; // 已归档项稍微半透明，与未归档项形成清晰视觉差异
            }
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => false;
    }

    public class WordStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int state)
            {
                return (WordLearningState)state switch
                {
                    WordLearningState.New => new SolidColorBrush(Color.FromArgb(255, 59, 130, 246)),      // 蓝
                    WordLearningState.Learning => new SolidColorBrush(Color.FromArgb(255, 234, 179, 8)),  // 黄
                    WordLearningState.Review => new SolidColorBrush(Color.FromArgb(255, 168, 85, 247)),   // 紫
                    WordLearningState.Mastered => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),  // 绿
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0;
    }

    public class WordStateToNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            if (value is int state)
            {
                return (WordLearningState)state switch
                {
                    WordLearningState.New => loc.GetString("StateNew", "未学习"),
                    WordLearningState.Learning => loc.GetString("StateLearning", "初学中"),
                    WordLearningState.Review => loc.GetString("StateReview", "复习中"),
                    WordLearningState.Mastered => loc.GetString("StateMastered", "已掌握"),
                    _ => loc.GetString("StateUnknown", "未知")
                };
            }
            if (value is WordLearningState wordState)
            {
                return wordState switch
                {
                    WordLearningState.New => loc.GetString("StateNew", "未学习"),
                    WordLearningState.Learning => loc.GetString("StateLearning", "初学中"),
                    WordLearningState.Review => loc.GetString("StateReview", "复习中"),
                    WordLearningState.Mastered => loc.GetString("StateMastered", "已掌握"),
                    _ => loc.GetString("StateUnknown", "未知")
                };
            }
            return loc.GetString("StateUnknown", "未知");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0;
    }

    public static class ThemeHelper
    {
        public static bool IsCurrentDarkTheme()
        {
            try
            {
                if (App.MainWindowInstance?.Content is FrameworkElement fe)
                {
                    if (fe.RequestedTheme == ElementTheme.Dark) return true;
                    if (fe.RequestedTheme == ElementTheme.Light) return false;
                    return fe.ActualTheme == ElementTheme.Dark;
                }
                string savedTheme = UserPreferenceService.Instance.Get("App_Theme", "Default");
                if (savedTheme == "Dark") return true;
                if (savedTheme == "Light") return false;
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }
            catch
            {
                return false;
            }
        }
    }

    public class CardStateToBorderBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isDark = ThemeHelper.IsCurrentDarkTheme();
            if (value is CardInteractionState state)
            {
                return state switch
                {
                    CardInteractionState.PendingRemember => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)), // 墨绿高亮边框
                    CardInteractionState.PendingForget => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),   // 砖红高亮边框
                    _ => isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0))
                };
            }
            return isDark ? new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) : new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => CardInteractionState.Normal;
    }

    public class CardStateToBackgroundBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isDark = ThemeHelper.IsCurrentDarkTheme();
            if (value is CardInteractionState state)
            {
                return state switch
                {
                    CardInteractionState.PendingRemember => new SolidColorBrush(isDark ? Color.FromArgb(60, 34, 197, 94) : Color.FromArgb(40, 34, 197, 94)), // 墨绿淡底
                    CardInteractionState.PendingForget => new SolidColorBrush(isDark ? Color.FromArgb(60, 239, 68, 68) : Color.FromArgb(40, 239, 68, 68)),   // 砖红淡底
                    _ => isDark ? new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)) : new SolidColorBrush(Colors.White)
                };
            }
            return isDark ? new SolidColorBrush(Color.FromArgb(255, 43, 43, 43)) : new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => CardInteractionState.Normal;
    }

    public class PercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d) return $"{d:P1}";
            return "0.0%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0.0;
    }

    public class DateToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd");
            if (value is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd");
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => DateTime.Now;
    }

    public class WordCountSuffixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            return string.Format(loc.GetString("WordsSuffixFormat", "{0} 词"), value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0;
    }

    public class RepsCountSuffixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            return string.Format(loc.GetString("RepsCountSuffixFormat", "已复习 {0} 次"), value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0;
    }

    // 模块四：FSRS 指标通俗化转换器
    public class RetentionToTagConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            if (value is double ret)
            {
                if (ret <= 0.0) return $"0% - {loc.GetString("RetentionPending", "待学习")}";
                double pct = ret * 100.0;
                if (pct >= 90) return $"{pct:F0}% - {loc.GetString("RetentionSolid", "记忆牢固")}";
                if (pct >= 75) return $"{pct:F0}% - {loc.GetString("RetentionGood", "状态良好")}";
                if (pct >= 60) return $"{pct:F0}% - {loc.GetString("RetentionCritical", "临界遗忘，建议复习")}";
                return $"{pct:F0}% - {loc.GetString("RetentionUrgent", "急需复习强化")}";
            }
            return $"0% - {loc.GetString("RetentionPending", "待学习")}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0.0;
    }

    public class RetentionToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isDark = ThemeHelper.IsCurrentDarkTheme();
            if (value is double ret)
            {
                if (ret <= 0.0) return new SolidColorBrush(isDark ? Color.FromArgb(255, 156, 163, 175) : Color.FromArgb(255, 107, 114, 128)); // 中性灰
                double pct = ret * 100.0;
                if (pct >= 85)
                {
                    // 绿色系：深色模式使用高亮亮绿，浅色模式使用深翠绿
                    return new SolidColorBrush(isDark ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 21, 128, 61));
                }
                if (pct >= 65)
                {
                    // 黄/琥珀系：深色模式使用亮金黄，浅色模式使用深琥珀金棕 (告别白底黄字发虚)
                    return new SolidColorBrush(isDark ? Color.FromArgb(255, 251, 191, 36) : Color.FromArgb(255, 180, 83, 9));
                }
                // 红色系：深色模式使用亮红，浅色模式使用深红
                return new SolidColorBrush(isDark ? Color.FromArgb(255, 248, 113, 113) : Color.FromArgb(255, 220, 38, 38));
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => new SolidColorBrush(Colors.Gray);
    }

    public class StabilityToHalfLifeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            if (value is double s)
            {
                if (s <= 0) return loc.GetString("StabilityUnreviewed", "预计维持：尚未复习");
                if (s < 1) return string.Format(loc.GetString("StabilityHoursFormat", "预计维持：约 {0:F0} 小时"), s * 24);
                return string.Format(loc.GetString("StabilityDaysFormat", "预计维持：约 {0:F1} 天"), s);
            }
            return loc.GetString("StabilityUnreviewed", "预计维持：尚未复习");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0.0;
    }

    public class DifficultyToRatingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var loc = LocalizationService.Instance;
            if (value is double d)
            {
                if (d <= 0) return loc.GetString("DifficultyInitial", "难度：初始新词");
                if (d <= 3.5) return string.Format(loc.GetString("DifficultyEasyFormat", "难度：{0:F1} (较易)"), d);
                if (d <= 6.5) return string.Format(loc.GetString("DifficultyMediumFormat", "难度：{0:F1} (适中)"), d);
                return string.Format(loc.GetString("DifficultyHardFormat", "难度：{0:F1} (较难)"), d);
            }
            return loc.GetString("DifficultyInitial", "难度：初始新词");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => 0.0;
    }

    public class StringFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null) return string.Empty;
            if (parameter is string format && !string.IsNullOrEmpty(format))
            {
                if (format.Contains("{0"))
                {
                    return string.Format(format, value);
                }
                if (value is IFormattable formattable)
                {
                    return formattable.ToString(format, null);
                }
                return string.Format("{0:" + format + "}", value);
            }
            return value.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => value;
    }
}
