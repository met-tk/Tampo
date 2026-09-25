using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Tasks;

namespace NihongoVocab.Services
{
    /// <summary>
    /// 全局用户操作状态与首选项服务：提供内存级快速读写与后台 SQLite 异步持久化
    /// 支持下拉选择、树维度、分割条布局、窗口尺寸位置等跨会话记忆
    /// </summary>
    public class UserPreferenceService
    {
        private static UserPreferenceService? _instance;
        public static UserPreferenceService Instance => _instance ??= App.GetService<UserPreferenceService>();

        private readonly DatabaseService _databaseService;
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
        private bool _isLoaded = false;

        public UserPreferenceService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        /// <summary>
        /// 预加载所有持久化的偏好设置到内存高速缓存
        /// </summary>
        public async Task LoadAllAsync()
        {
            if (_isLoaded) return;
            try
            {
                var list = await _databaseService.GetAllPreferencesAsync();
                foreach (var item in list)
                {
                    _cache[item.Key] = item.Value;
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "UserPreferenceService.LoadAllAsync");
            }
        }

        public string Get(string key, string defaultValue = "")
        {
            return _cache.TryGetValue(key, out var val) ? val : defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (_cache.TryGetValue(key, out var val) && int.TryParse(val, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        public double GetDouble(string key, double defaultValue = 0.0)
        {
            if (_cache.TryGetValue(key, out var val) &&
                double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (_cache.TryGetValue(key, out var val) && bool.TryParse(val, out var parsed))
            {
                return parsed;
            }
            return defaultValue;
        }

        public void Set(string key, string value)
        {
            _cache[key] = value;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _databaseService.SetPreferenceAsync(key, value);
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, $"UserPreferenceService.Set({key})");
                }
            });
        }

        public void SetInt(string key, int value) => Set(key, value.ToString());
        public void SetDouble(string key, double value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
        public void SetBool(string key, bool value) => Set(key, value.ToString());
    }
}
