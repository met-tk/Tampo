using System;
using System.Threading.Tasks;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class DataRefreshGateTests
    {
        [Fact]
        public async Task RefreshIfNeededAsync_SkipsUnchangedDataAndRefreshesAfterRevisionChanges()
        {
            long revision = 0;
            int refreshCount = 0;
            var gate = new DataRefreshGate(() => revision);
            Task Refresh()
            {
                refreshCount++;
                return Task.CompletedTask;
            }

            await gate.RefreshIfNeededAsync(Refresh);
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(1, refreshCount);

            revision++;
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(2, refreshCount);

            await gate.RefreshIfNeededAsync(Refresh, force: true);
            Assert.Equal(3, refreshCount);
        }

        [Fact]
        public async Task RefreshIfNeededAsync_RetriesAfterFailedLoad()
        {
            var gate = new DataRefreshGate(() => 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gate.RefreshIfNeededAsync(() => throw new InvalidOperationException("load failed")));

            int retryCount = 0;
            await gate.RefreshIfNeededAsync(() =>
            {
                retryCount++;
                return Task.CompletedTask;
            });
            Assert.Equal(1, retryCount);
        }

        [Fact]
        public async Task RefreshIfNeededAsync_RefreshesTimeSensitiveViewsWhenCacheExpires()
        {
            var now = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
            var gate = new DataRefreshGate(() => 0, TimeSpan.FromMinutes(1), () => now);
            int refreshCount = 0;
            Task Refresh()
            {
                refreshCount++;
                return Task.CompletedTask;
            }

            await gate.RefreshIfNeededAsync(Refresh);
            now = now.AddSeconds(59);
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(1, refreshCount);

            now = now.AddSeconds(1);
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(2, refreshCount);
        }

        [Fact]
        public async Task RefreshIfNeededAsync_RefreshesOnLogicalDayChangeOnly()
        {
            long logicalDay = 1;
            int refreshCount = 0;
            var gate = new DataRefreshGate(() => 0, getPeriod: () => logicalDay);
            Task Refresh()
            {
                refreshCount++;
                return Task.CompletedTask;
            }

            await gate.RefreshIfNeededAsync(Refresh);
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(1, refreshCount);

            logicalDay++;
            await gate.RefreshIfNeededAsync(Refresh);
            Assert.Equal(2, refreshCount);
        }
    }
}
