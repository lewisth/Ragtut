using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ragtut.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ragtut.Core.Services;

/// <summary>
/// Service for monitoring performance metrics and operation timings
/// </summary>
public class PerformanceMonitoringService : IDisposable
{
    private readonly PerformanceConfig _config;
    private readonly ILogger<PerformanceMonitoringService> _logger;
    private readonly ConcurrentDictionary<string, OperationMetrics> _metrics = new();
    private readonly Timer? _metricsTimer;
    private bool _disposed;

    public PerformanceMonitoringService(IOptions<RagConfiguration> options, ILogger<PerformanceMonitoringService> logger)
    {
        _config = options.Value.Performance;
        _logger = logger;

        if (_config.EnableMetrics)
        {
            _metricsTimer = new Timer(LogMetrics, null, 
                TimeSpan.FromSeconds(_config.MetricsIntervalSeconds),
                TimeSpan.FromSeconds(_config.MetricsIntervalSeconds));
        }
    }

    /// <summary>
    /// Measures the execution time of an operation
    /// </summary>
    public async Task<T> MeasureAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        if (!_config.EnableMetrics && !IsTrackedOperation(operationName))
        {
            return await operation();
        }

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            var result = await operation();
            stopwatch.Stop();

            RecordSuccess(operationName, stopwatch.ElapsedMilliseconds, startTime);
            
            if (_config.LogSlowOperations && stopwatch.ElapsedMilliseconds > _config.SlowOperationThresholdMs)
            {
                _logger.LogWarning("Slow operation detected: {OperationName} took {Duration}ms", 
                    operationName, stopwatch.ElapsedMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(operationName, stopwatch.ElapsedMilliseconds, startTime, ex);
            throw;
        }
    }

    /// <summary>
    /// Measures the execution time of an operation (void return)
    /// </summary>
    public async Task MeasureAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await MeasureAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    /// <summary>
    /// Records a custom metric
    /// </summary>
    public void RecordMetric(string metricName, double value, Dictionary<string, string>? tags = null)
    {
        if (!_config.EnableMetrics) return;

        var key = GetMetricKey(metricName, tags);
        _metrics.AddOrUpdate(key, 
            new OperationMetrics(metricName, tags),
            (_, existing) =>
            {
                existing.RecordValue(value);
                return existing;
            });
    }

    /// <summary>
    /// Gets current metrics snapshot
    /// </summary>
    public Dictionary<string, object> GetMetricsSnapshot()
    {
        var snapshot = new Dictionary<string, object>();

        foreach (var kvp in _metrics)
        {
            var metrics = kvp.Value;
            snapshot[kvp.Key] = new
            {
                metrics.Name,
                metrics.Tags,
                metrics.Count,
                metrics.TotalDuration,
                AverageDuration = metrics.Count > 0 ? metrics.TotalDuration / metrics.Count : 0,
                metrics.MinDuration,
                metrics.MaxDuration,
                metrics.SuccessCount,
                metrics.FailureCount,
                SuccessRate = metrics.Count > 0 ? (double)metrics.SuccessCount / metrics.Count * 100 : 0,
                LastUpdated = metrics.LastUpdated
            };
        }

        return snapshot;
    }

    /// <summary>
    /// Resets all metrics
    /// </summary>
    public void ResetMetrics()
    {
        _metrics.Clear();
        _logger.LogInformation("Performance metrics have been reset");
    }

    private void RecordSuccess(string operationName, long durationMs, DateTimeOffset startTime)
    {
        if (!_config.EnableMetrics && !IsTrackedOperation(operationName)) return;

        var key = GetMetricKey(operationName);
        _metrics.AddOrUpdate(key,
            new OperationMetrics(operationName),
            (_, existing) =>
            {
                existing.RecordSuccess(durationMs, startTime);
                return existing;
            });
    }

    private void RecordFailure(string operationName, long durationMs, DateTimeOffset startTime, Exception exception)
    {
        if (!_config.EnableMetrics && !IsTrackedOperation(operationName)) return;

        var key = GetMetricKey(operationName);
        _metrics.AddOrUpdate(key,
            new OperationMetrics(operationName),
            (_, existing) =>
            {
                existing.RecordFailure(durationMs, startTime, exception);
                return existing;
            });

        _logger.LogError(exception, "Operation {OperationName} failed after {Duration}ms", 
            operationName, durationMs);
    }

    private bool IsTrackedOperation(string operationName)
    {
        return _config.TrackedOperations.Contains(operationName);
    }

    private string GetMetricKey(string name, Dictionary<string, string>? tags = null)
    {
        if (tags == null || tags.Count == 0)
            return name;

        var tagString = string.Join(",", tags.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{name}[{tagString}]";
    }

    private void LogMetrics(object? state)
    {
        try
        {
            if (_metrics.IsEmpty) return;

            _logger.LogInformation("=== Performance Metrics Report ===");

            foreach (var kvp in _metrics.OrderBy(x => x.Key))
            {
                var metrics = kvp.Value;
                var avgDuration = metrics.Count > 0 ? metrics.TotalDuration / (double)metrics.Count : 0;
                var successRate = metrics.Count > 0 ? metrics.SuccessCount / (double)metrics.Count * 100 : 0;

                _logger.LogInformation(
                    "{MetricName}: Count={Count}, Avg={AvgDuration:F1}ms, Min={MinDuration}ms, Max={MaxDuration}ms, Success={SuccessRate:F1}%",
                    metrics.Name,
                    metrics.Count,
                    avgDuration,
                    metrics.MinDuration,
                    metrics.MaxDuration,
                    successRate);
            }

            _logger.LogInformation("=== End Performance Metrics Report ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging performance metrics");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _metricsTimer?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Represents metrics for a specific operation
/// </summary>
public class OperationMetrics
{
    private readonly object _lock = new();

    public string Name { get; }
    public Dictionary<string, string>? Tags { get; }
    public long Count { get; private set; }
    public long TotalDuration { get; private set; }
    public long MinDuration { get; private set; } = long.MaxValue;
    public long MaxDuration { get; private set; }
    public long SuccessCount { get; private set; }
    public long FailureCount { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; } = DateTimeOffset.UtcNow;
    public Exception? LastException { get; private set; }

    public OperationMetrics(string name, Dictionary<string, string>? tags = null)
    {
        Name = name;
        Tags = tags;
    }

    public void RecordSuccess(long durationMs, DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            Count++;
            SuccessCount++;
            TotalDuration += durationMs;
            MinDuration = Math.Min(MinDuration, durationMs);
            MaxDuration = Math.Max(MaxDuration, durationMs);
            LastUpdated = timestamp;
        }
    }

    public void RecordFailure(long durationMs, DateTimeOffset timestamp, Exception exception)
    {
        lock (_lock)
        {
            Count++;
            FailureCount++;
            TotalDuration += durationMs;
            MinDuration = Math.Min(MinDuration, durationMs);
            MaxDuration = Math.Max(MaxDuration, durationMs);
            LastUpdated = timestamp;
            LastException = exception;
        }
    }

    public void RecordValue(double value)
    {
        lock (_lock)
        {
            Count++;
            TotalDuration += (long)value;
            MinDuration = Math.Min(MinDuration, (long)value);
            MaxDuration = Math.Max(MaxDuration, (long)value);
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }
} 