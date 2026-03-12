using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ragtut.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;

namespace Ragtut.Core.Services;

/// <summary>
/// Service for managing memory usage and garbage collection optimization
/// </summary>
public class MemoryManagementService : IDisposable
{
    private readonly MemoryConfig _config;
    private readonly ILogger<MemoryManagementService> _logger;
    private readonly Timer? _memoryMonitorTimer;
    private readonly ConcurrentDictionary<string, WeakReference> _documentCache = new();
    private readonly ConcurrentDictionary<string, WeakReference> _embeddingCache = new();
    private readonly List<Func<Task>> _memoryPressureCallbacks = new();
    private readonly object _documentCacheCleanupLock = new();
    private readonly object _embeddingCacheCleanupLock = new();
    private long _documentCacheSizeBytes;
    private long _embeddingCacheSizeBytes;
    private bool _disposed;

    public event EventHandler<MemoryPressureEventArgs>? MemoryPressureDetected;

    public MemoryManagementService(IOptions<RagConfiguration> options, ILogger<MemoryManagementService> logger)
    {
        _config = options.Value.Memory;
        _logger = logger;

        if (_config.EnableMemoryPressureCallback)
        {
            _memoryMonitorTimer = new Timer(MonitorMemoryUsage, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        if (_config.EnableGarbageCollectionTuning)
        {
            ConfigureGarbageCollection();
        }
    }

    /// <summary>
    /// Caches a document with size tracking
    /// </summary>
    public void CacheDocument(string key, object document, long estimatedSizeBytes)
    {
        if (estimatedSizeBytes > _config.MaxDocumentCacheSizeMB * 1024 * 1024)
        {
            _logger.LogWarning("Document {Key} size ({Size} bytes) exceeds cache limit", key, estimatedSizeBytes);
            return;
        }

        CleanupDocumentCache();

        var newSize = Interlocked.Add(ref _documentCacheSizeBytes, estimatedSizeBytes);
        if (newSize > _config.MaxDocumentCacheSizeMB * 1024 * 1024)
        {
            Interlocked.Add(ref _documentCacheSizeBytes, -estimatedSizeBytes);
            TriggerCacheCleanup("DocumentCache");
            return;
        }

        _documentCache[key] = new WeakReference(new CachedItem(document, estimatedSizeBytes));
        _logger.LogDebug("Cached document {Key}, cache size: {Size} MB", 
            key, newSize / (1024.0 * 1024.0));
    }

    /// <summary>
    /// Retrieves a cached document
    /// </summary>
    public T? GetCachedDocument<T>(string key) where T : class
    {
        if (_documentCache.TryGetValue(key, out var weakRef) && weakRef.IsAlive)
        {
            if (weakRef.Target is CachedItem cachedItem)
            {
                return cachedItem.Data as T;
            }
        }

        _documentCache.TryRemove(key, out _);
        return null;
    }

    /// <summary>
    /// Caches embeddings with size tracking
    /// </summary>
    public void CacheEmbedding(string key, float[] embedding)
    {
        var estimatedSize = embedding.Length * sizeof(float);
        
        if (estimatedSize > _config.MaxEmbeddingCacheSizeMB * 1024 * 1024)
        {
            _logger.LogWarning("Embedding {Key} size ({Size} bytes) exceeds cache limit", key, estimatedSize);
            return;
        }

        CleanupEmbeddingCache();

        var newSize = Interlocked.Add(ref _embeddingCacheSizeBytes, estimatedSize);
        if (newSize > _config.MaxEmbeddingCacheSizeMB * 1024 * 1024)
        {
            Interlocked.Add(ref _embeddingCacheSizeBytes, -estimatedSize);
            TriggerCacheCleanup("EmbeddingCache");
            return;
        }

        _embeddingCache[key] = new WeakReference(new CachedItem(embedding, estimatedSize));
        _logger.LogDebug("Cached embedding {Key}, cache size: {Size} MB", 
            key, newSize / (1024.0 * 1024.0));
    }

    /// <summary>
    /// Retrieves cached embeddings
    /// </summary>
    public float[]? GetCachedEmbedding(string key)
    {
        if (_embeddingCache.TryGetValue(key, out var weakRef) && weakRef.IsAlive)
        {
            if (weakRef.Target is CachedItem cachedItem)
            {
                return cachedItem.Data as float[];
            }
        }

        _embeddingCache.TryRemove(key, out _);
        return null;
    }

    /// <summary>
    /// Registers a callback to be invoked during memory pressure
    /// </summary>
    public void RegisterMemoryPressureCallback(Func<Task> callback)
    {
        _memoryPressureCallbacks.Add(callback);
    }

    /// <summary>
    /// Forces garbage collection and cache cleanup
    /// </summary>
    public void ForceCleanup()
    {
        _logger.LogInformation("Forcing memory cleanup");

        CleanupDocumentCache();
        CleanupEmbeddingCache();

        if (_config.EnableGarbageCollectionTuning)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        LogMemoryUsage("After forced cleanup");
    }

    /// <summary>
    /// Gets current memory usage statistics
    /// </summary>
    public MemoryUsageStats GetMemoryUsage()
    {
        var process = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();

        return new MemoryUsageStats
        {
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            ManagedMemoryBytes = GC.GetTotalMemory(false),
            DocumentCacheSizeBytes = _documentCacheSizeBytes,
            EmbeddingCacheSizeBytes = _embeddingCacheSizeBytes,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            HeapSizeBytes = gcInfo.HeapSizeBytes,
            MemoryLoadBytes = gcInfo.MemoryLoadBytes
        };
    }

    private void MonitorMemoryUsage(object? state)
    {
        try
        {
            var stats = GetMemoryUsage();
            var workingSetMB = stats.WorkingSetBytes / (1024.0 * 1024.0);
            var managedMemoryMB = stats.ManagedMemoryBytes / (1024.0 * 1024.0);

            // Check for memory pressure based on working set
            var memoryPressureThreshold = GetMemoryPressureThreshold();
            if (stats.WorkingSetBytes > memoryPressureThreshold)
            {
                var pressureLevel = CalculateMemoryPressureLevel(stats.WorkingSetBytes, memoryPressureThreshold);
                
                _logger.LogWarning("Memory pressure detected: Working Set = {WorkingSet:F1} MB, Managed = {Managed:F1} MB, Pressure Level = {PressureLevel}",
                    workingSetMB, managedMemoryMB, pressureLevel);

                var eventArgs = new MemoryPressureEventArgs(stats, pressureLevel);
                MemoryPressureDetected?.Invoke(this, eventArgs);

                _ = Task.Run(async () => await HandleMemoryPressure(pressureLevel));
            }

            // Log periodic memory statistics
            _logger.LogDebug("Memory usage: Working Set = {WorkingSet:F1} MB, Managed = {Managed:F1} MB, " +
                            "Document Cache = {DocCache:F1} MB, Embedding Cache = {EmbCache:F1} MB",
                workingSetMB, managedMemoryMB,
                _documentCacheSizeBytes / (1024.0 * 1024.0),
                _embeddingCacheSizeBytes / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring memory usage");
        }
    }

    private async Task HandleMemoryPressure(MemoryPressureLevel pressureLevel)
    {
        _logger.LogInformation("Handling memory pressure level: {PressureLevel}", pressureLevel);

        // Execute registered callbacks
        foreach (var callback in _memoryPressureCallbacks)
        {
            try
            {
                await callback();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing memory pressure callback");
            }
        }

        // Perform cleanup based on pressure level
        switch (pressureLevel)
        {
            case MemoryPressureLevel.Moderate:
                CleanupDocumentCache(0.3); // Clean 30% of document cache
                CleanupEmbeddingCache(0.2); // Clean 20% of embedding cache
                break;

            case MemoryPressureLevel.High:
                CleanupDocumentCache(0.6); // Clean 60% of document cache
                CleanupEmbeddingCache(0.5); // Clean 50% of embedding cache
                if (_config.EnableGarbageCollectionTuning)
                {
                    GC.Collect(1, GCCollectionMode.Forced);
                }
                break;

            case MemoryPressureLevel.Critical:
                CleanupDocumentCache(0.8); // Clean 80% of document cache
                CleanupEmbeddingCache(0.7); // Clean 70% of embedding cache
                if (_config.EnableGarbageCollectionTuning)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                }
                break;
        }

        LogMemoryUsage($"After {pressureLevel} memory pressure handling");
    }

    private void CleanupDocumentCache(double removalRatio = 1.0)
    {
        lock (_documentCacheCleanupLock)
        {
            var keysToRemove = new List<string>();
            var targetRemovalCount = (int)(_documentCache.Count * removalRatio);

            foreach (var kvp in _documentCache.Take(targetRemovalCount))
            {
                if (!kvp.Value.IsAlive)
                {
                    keysToRemove.Add(kvp.Key);
                }
                else if (removalRatio < 1.0)
                {
                    keysToRemove.Add(kvp.Key);
                    if (kvp.Value.Target is CachedItem item)
                    {
                        Interlocked.Add(ref _documentCacheSizeBytes, -item.SizeBytes);
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                _documentCache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} document cache entries", keysToRemove.Count);
            }
        }
    }

    private void CleanupEmbeddingCache(double removalRatio = 1.0)
    {
        lock (_embeddingCacheCleanupLock)
        {
            var keysToRemove = new List<string>();
            var targetRemovalCount = (int)(_embeddingCache.Count * removalRatio);

            foreach (var kvp in _embeddingCache.Take(targetRemovalCount))
            {
                if (!kvp.Value.IsAlive)
                {
                    keysToRemove.Add(kvp.Key);
                }
                else if (removalRatio < 1.0)
                {
                    keysToRemove.Add(kvp.Key);
                    if (kvp.Value.Target is CachedItem item)
                    {
                        Interlocked.Add(ref _embeddingCacheSizeBytes, -item.SizeBytes);
                    }
                }
            }

            foreach (var key in keysToRemove)
            {
                _embeddingCache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} embedding cache entries", keysToRemove.Count);
            }
        }
    }

    private void TriggerCacheCleanup(string cacheType)
    {
        _logger.LogWarning("Cache limit exceeded for {CacheType}, triggering cleanup", cacheType);
        
        if (cacheType == "DocumentCache")
        {
            CleanupDocumentCache(0.3);
        }
        else if (cacheType == "EmbeddingCache")
        {
            CleanupEmbeddingCache(0.3);
        }
    }

    private void ConfigureGarbageCollection()
    {
        // Configure garbage collection for better memory management
        GCSettings.LatencyMode = GCLatencyMode.Batch;
        _logger.LogInformation("Configured garbage collection for batch mode");
    }

    private long GetMemoryPressureThreshold()
    {
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (long)(totalMemory * (_config.MemoryWarningThresholdPercent / 100.0));
    }

    private MemoryPressureLevel CalculateMemoryPressureLevel(long currentMemory, long threshold)
    {
        var ratio = (double)currentMemory / threshold;
        
        return ratio switch
        {
            >= 1.5 => MemoryPressureLevel.Critical,
            >= 1.2 => MemoryPressureLevel.High,
            >= 1.0 => MemoryPressureLevel.Moderate,
            _ => MemoryPressureLevel.Low
        };
    }

    private void LogMemoryUsage(string context)
    {
        var stats = GetMemoryUsage();
        _logger.LogInformation("{Context}: Working Set = {WorkingSet:F1} MB, Managed = {Managed:F1} MB, " +
                              "GC Gen0/1/2 = {Gen0}/{Gen1}/{Gen2}",
            context,
            stats.WorkingSetBytes / (1024.0 * 1024.0),
            stats.ManagedMemoryBytes / (1024.0 * 1024.0),
            stats.Gen0Collections,
            stats.Gen1Collections,
            stats.Gen2Collections);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _memoryMonitorTimer?.Dispose();
        _documentCache.Clear();
        _embeddingCache.Clear();
        _disposed = true;
    }
}

/// <summary>
/// Represents a cached item with size information
/// </summary>
public class CachedItem
{
    public object Data { get; }
    public long SizeBytes { get; }

    public CachedItem(object data, long sizeBytes)
    {
        Data = data;
        SizeBytes = sizeBytes;
    }
}

/// <summary>
/// Memory usage statistics
/// </summary>
public class MemoryUsageStats
{
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long ManagedMemoryBytes { get; set; }
    public long DocumentCacheSizeBytes { get; set; }
    public long EmbeddingCacheSizeBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long HeapSizeBytes { get; set; }
    public long MemoryLoadBytes { get; set; }
}

/// <summary>
/// Memory pressure levels
/// </summary>
public enum MemoryPressureLevel
{
    Low,
    Moderate,
    High,
    Critical
}

/// <summary>
/// Event args for memory pressure events
/// </summary>
public class MemoryPressureEventArgs : EventArgs
{
    public MemoryUsageStats Stats { get; }
    public MemoryPressureLevel PressureLevel { get; }

    public MemoryPressureEventArgs(MemoryUsageStats stats, MemoryPressureLevel pressureLevel)
    {
        Stats = stats;
        PressureLevel = pressureLevel;
    }
} 