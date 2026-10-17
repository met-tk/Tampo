using System;
using System.Threading;
using System.Threading.Tasks;

namespace NihongoVocab.Services
{
    /// <summary>跳过未变更数据的重复页面加载，并合并并发刷新。</summary>
    public sealed class DataRefreshGate
    {
        private readonly SemaphoreSlim _mutex = new(1, 1);
        private readonly Func<long> _getRevision;
        private readonly Func<DateTimeOffset> _getTime;
        private readonly Func<long> _getPeriod;
        private readonly TimeSpan? _maxAge;
        private long _lastLoadedRevision = -1;
        private long _lastLoadedPeriod = long.MinValue;
        private DateTimeOffset _lastLoadedAt;

        public DataRefreshGate(TimeSpan? maxAge = null) : this(() => DatabaseService.DataRevision, maxAge) { }

        public DataRefreshGate(Func<long> getRevision, TimeSpan? maxAge = null, Func<DateTimeOffset>? getTime = null,
            Func<long>? getPeriod = null)
        {
            _getRevision = getRevision ?? throw new ArgumentNullException(nameof(getRevision));
            _getTime = getTime ?? (() => DateTimeOffset.UtcNow);
            _getPeriod = getPeriod ?? (() => 0);
            _maxAge = maxAge;
        }

        public async Task RefreshIfNeededAsync(Func<Task> refresh, bool force = false)
        {
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));
            await _mutex.WaitAsync();
            try
            {
                long revision = _getRevision();
                long period = _getPeriod();
                if (!force && revision == _lastLoadedRevision && period == _lastLoadedPeriod &&
                    (!_maxAge.HasValue || _getTime() - _lastLoadedAt < _maxAge.Value)) return;
                await refresh();
                _lastLoadedRevision = revision;
                _lastLoadedPeriod = period;
                _lastLoadedAt = _getTime();
            }
            finally
            {
                _mutex.Release();
            }
        }
    }
}
