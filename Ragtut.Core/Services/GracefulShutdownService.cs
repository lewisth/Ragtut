using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ragtut.Core.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Ragtut.Core.Services;

/// <summary>
/// Service for handling graceful application shutdown
/// </summary>
public class GracefulShutdownService : IHostedService, IDisposable
{
    private readonly ShutdownConfig _config;
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly CancellationTokenSource _shutdownTokenSource = new();
    private readonly ConcurrentDictionary<string, TaskInfo> _activeTasks = new();
    private readonly List<Func<CancellationToken, Task>> _shutdownCallbacks = new();
    private readonly List<Func<Task<object>>> _stateProviders = new();
    private bool _shutdownRequested;
    private bool _disposed;

    public event EventHandler<ShutdownEventArgs>? ShutdownInitiated;
    public event EventHandler<ShutdownEventArgs>? ShutdownCompleted;

    public bool IsShuttingDown => _shutdownRequested;
    public CancellationToken ShutdownToken => _shutdownTokenSource.Token;

    public GracefulShutdownService(IOptions<RagConfiguration> options, ILogger<GracefulShutdownService> logger)
    {
        _config = options.Value.Shutdown;
        _logger = logger;
    }

    /// <summary>
    /// Starts the graceful shutdown service
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown service started");
        
        // Register for application shutdown events
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the graceful shutdown service
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await InitiateShutdownAsync();
    }

    /// <summary>
    /// Registers a task as active
    /// </summary>
    public void RegisterActiveTask(string taskId, string description, CancellationToken cancellationToken = default)
    {
        if (_shutdownRequested)
        {
            throw new InvalidOperationException("Cannot register tasks during shutdown");
        }

        var taskInfo = new TaskInfo(taskId, description, DateTimeOffset.UtcNow, cancellationToken);
        _activeTasks[taskId] = taskInfo;
        
        _logger.LogDebug("Registered active task: {TaskId} - {Description}", taskId, description);
    }

    /// <summary>
    /// Unregisters an active task
    /// </summary>
    public void UnregisterActiveTask(string taskId)
    {
        if (_activeTasks.TryRemove(taskId, out var taskInfo))
        {
            var duration = DateTimeOffset.UtcNow - taskInfo.StartTime;
            _logger.LogDebug("Unregistered active task: {TaskId} - {Description} (Duration: {Duration}ms)",
                taskId, taskInfo.Description, duration.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Executes a task with automatic registration/unregistration
    /// </summary>
    public async Task<T> ExecuteTaskAsync<T>(
        string taskId,
        string description,
        Func<CancellationToken, Task<T>> taskFunc,
        CancellationToken cancellationToken = default)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdownTokenSource.Token);

        RegisterActiveTask(taskId, description, combinedCts.Token);
        try
        {
            return await taskFunc(combinedCts.Token);
        }
        finally
        {
            UnregisterActiveTask(taskId);
        }
    }

    /// <summary>
    /// Executes a task with automatic registration/unregistration (void return)
    /// </summary>
    public async Task ExecuteTaskAsync(
        string taskId,
        string description,
        Func<CancellationToken, Task> taskFunc,
        CancellationToken cancellationToken = default)
    {
        await ExecuteTaskAsync(taskId, description, async ct =>
        {
            await taskFunc(ct);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Registers a callback to be executed during shutdown
    /// </summary>
    public void RegisterShutdownCallback(Func<CancellationToken, Task> callback)
    {
        _shutdownCallbacks.Add(callback);
    }

    /// <summary>
    /// Registers a state provider for shutdown state saving
    /// </summary>
    public void RegisterStateProvider(Func<Task<object>> stateProvider)
    {
        _stateProviders.Add(stateProvider);
    }

    /// <summary>
    /// Gets information about currently active tasks
    /// </summary>
    public IReadOnlyCollection<TaskInfo> GetActiveTasks()
    {
        return _activeTasks.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Initiates graceful shutdown
    /// </summary>
    public async Task InitiateShutdownAsync()
    {
        if (_shutdownRequested)
        {
            _logger.LogWarning("Shutdown already requested");
            return;
        }

        _shutdownRequested = true;
        var shutdownStartTime = DateTimeOffset.UtcNow;

        _logger.LogInformation("Initiating graceful shutdown...");
        
        var eventArgs = new ShutdownEventArgs(shutdownStartTime, _activeTasks.Count);
        ShutdownInitiated?.Invoke(this, eventArgs);

        try
        {
            // Signal shutdown to all components
            _shutdownTokenSource.Cancel();

            // Execute shutdown callbacks
            await ExecuteShutdownCallbacks();

            // Wait for active operations to complete
            if (_config.WaitForActiveOperations)
            {
                await WaitForActiveOperations();
            }

            // Save state if configured
            if (_config.SaveStateOnShutdown)
            {
                await SaveShutdownState();
            }

            var shutdownDuration = DateTimeOffset.UtcNow - shutdownStartTime;
            _logger.LogInformation("Graceful shutdown completed in {Duration}ms", shutdownDuration.TotalMilliseconds);

            if (_config.EnableShutdownMetrics)
            {
                LogShutdownMetrics(shutdownDuration);
            }

            var completedEventArgs = new ShutdownEventArgs(shutdownStartTime, 0, shutdownDuration);
            ShutdownCompleted?.Invoke(this, completedEventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during graceful shutdown");
            throw;
        }
    }

    private async Task ExecuteShutdownCallbacks()
    {
        _logger.LogInformation("Executing {Count} shutdown callbacks", _shutdownCallbacks.Count);

        var tasks = _shutdownCallbacks.Select(async callback =>
        {
            try
            {
                await callback(_shutdownTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing shutdown callback");
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task WaitForActiveOperations()
    {
        if (_activeTasks.IsEmpty)
        {
            _logger.LogInformation("No active operations to wait for");
            return;
        }

        _logger.LogInformation("Waiting for {Count} active operations to complete", _activeTasks.Count);

        var timeout = TimeSpan.FromSeconds(_config.GracefulShutdownTimeoutSeconds);
        var startTime = DateTimeOffset.UtcNow;

        while (!_activeTasks.IsEmpty && DateTimeOffset.UtcNow - startTime < timeout)
        {
            // Log current active tasks
            var activeTasks = _activeTasks.Values.ToList();
            _logger.LogInformation("Still waiting for {Count} active operations:", activeTasks.Count);
            
            foreach (var task in activeTasks.Take(5)) // Log up to 5 tasks
            {
                var duration = DateTimeOffset.UtcNow - task.StartTime;
                _logger.LogInformation("  - {TaskId}: {Description} (running for {Duration}ms)",
                    task.Id, task.Description, duration.TotalMilliseconds);
            }

            if (activeTasks.Count > 5)
            {
                _logger.LogInformation("  ... and {Count} more tasks", activeTasks.Count - 5);
            }

            await Task.Delay(1000, CancellationToken.None);
        }

        if (!_activeTasks.IsEmpty)
        {
            _logger.LogWarning("Timeout waiting for {Count} active operations to complete", _activeTasks.Count);
        }
        else
        {
            _logger.LogInformation("All active operations completed successfully");
        }
    }

    private async Task SaveShutdownState()
    {
        try
        {
            _logger.LogInformation("Saving shutdown state to {FilePath}", _config.StateFilePath);

            var state = new Dictionary<string, object>();

            // Collect state from providers
            foreach (var provider in _stateProviders)
            {
                try
                {
                    var providerState = await provider();
                    var providerName = provider.Method.DeclaringType?.Name ?? "Unknown";
                    state[providerName] = providerState;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error collecting state from provider");
                }
            }

            // Add shutdown metadata
            state["ShutdownMetadata"] = new
            {
                Timestamp = DateTimeOffset.UtcNow,
                Version = typeof(GracefulShutdownService).Assembly.GetName().Version?.ToString(),
                ActiveTaskCount = _activeTasks.Count,
                ActiveTasks = _activeTasks.Values.Select(t => new
                {
                    t.Id,
                    t.Description,
                    Duration = DateTimeOffset.UtcNow - t.StartTime
                }).ToList()
            };

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_config.StateFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save state to file
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(_config.StateFilePath, json, CancellationToken.None);
            _logger.LogInformation("Shutdown state saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving shutdown state");
        }
    }

    private void LogShutdownMetrics(TimeSpan shutdownDuration)
    {
        _logger.LogInformation("=== Shutdown Metrics ===");
        _logger.LogInformation("Total shutdown duration: {Duration}ms", shutdownDuration.TotalMilliseconds);
        _logger.LogInformation("Shutdown callbacks executed: {Count}", _shutdownCallbacks.Count);
        _logger.LogInformation("State providers executed: {Count}", _stateProviders.Count);
        _logger.LogInformation("Remaining active tasks: {Count}", _activeTasks.Count);
        _logger.LogInformation("========================");
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _logger.LogInformation("Process exit detected, initiating graceful shutdown");
        try
        {
            InitiateShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during process exit shutdown");
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        _logger.LogInformation("Ctrl+C detected, initiating graceful shutdown");
        e.Cancel = true; // Prevent immediate termination
        
        _ = Task.Run(async () =>
        {
            try
            {
                await InitiateShutdownAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Ctrl+C shutdown");
            }
            finally
            {
                Environment.Exit(0);
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Console.CancelKeyPress -= OnCancelKeyPress;

        _shutdownTokenSource.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Information about an active task
/// </summary>
public class TaskInfo
{
    public string Id { get; }
    public string Description { get; }
    public DateTimeOffset StartTime { get; }
    public CancellationToken CancellationToken { get; }

    public TaskInfo(string id, string description, DateTimeOffset startTime, CancellationToken cancellationToken)
    {
        Id = id;
        Description = description;
        StartTime = startTime;
        CancellationToken = cancellationToken;
    }
}

/// <summary>
/// Event args for shutdown events
/// </summary>
public class ShutdownEventArgs : EventArgs
{
    public DateTimeOffset StartTime { get; }
    public int ActiveTaskCount { get; }
    public TimeSpan? Duration { get; }

    public ShutdownEventArgs(DateTimeOffset startTime, int activeTaskCount, TimeSpan? duration = null)
    {
        StartTime = startTime;
        ActiveTaskCount = activeTaskCount;
        Duration = duration;
    }
} 