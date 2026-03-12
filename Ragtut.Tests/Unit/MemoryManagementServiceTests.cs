using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Ragtut.Core.Models;
using Ragtut.Core.Services;
using Shouldly;
using Xunit;

namespace Ragtut.Tests.Unit;

public class MemoryManagementServiceTests
{
    private MemoryManagementService CreateService(int maxDocumentCacheSizeMB = 1)
    {
        var config = new RagConfiguration
        {
            Memory = new MemoryConfig
            {
                MaxDocumentCacheSizeMB = maxDocumentCacheSizeMB,
                MaxEmbeddingCacheSizeMB = 200,
                EnableMemoryPressureCallback = false,
                EnableGarbageCollectionTuning = false,
                MemoryWarningThresholdPercent = 80
            }
        };

        var options = Substitute.For<IOptions<RagConfiguration>>();
        options.Value.Returns(config);
        var logger = Substitute.For<ILogger<MemoryManagementService>>();

        return new MemoryManagementService(options, logger);
    }

    [Fact]
    public async Task CleanupDocumentCache_WhenCalledConcurrently_ShouldNotCorruptSizeTracking()
    {
        // 1MB cache limit
        using var sut = CreateService(maxDocumentCacheSizeMB: 1);

        // Pre-populate cache to ~900KB (just under the 1MB limit)
        const int itemSizeBytes = 100 * 1024; // 100KB each
        for (int i = 0; i < 9; i++)
        {
            sut.CacheDocument($"initial-{i}", new object(), itemSizeBytes);
        }

        // Concurrently add 200KB items — each will push over the 1MB limit,
        // roll back its own addition, and trigger TriggerCacheCleanup → CleanupDocumentCache(0.3).
        // Without a lock, multiple threads both decrement the same items' sizes before
        // TryRemove tells them the item is already gone, corrupting the size counter.
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            sut.CacheDocument($"concurrent-{i}", new object(), 200 * 1024);
        })).ToArray();

        await Task.WhenAll(tasks);

        // The size counter must never go negative due to double-decrement in concurrent cleanup
        var stats = sut.GetMemoryUsage();
        stats.DocumentCacheSizeBytes.ShouldBeGreaterThanOrEqualTo(0,
            "Concurrent partial cleanups double-decremented _documentCacheSizeBytes, making it negative");
    }
}
