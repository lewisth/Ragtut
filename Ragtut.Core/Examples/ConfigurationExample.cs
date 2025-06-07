using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Extensions;
using Ragtut.Core.Models;
using Ragtut.Core.Services;

namespace Ragtut.Core.Examples;

/// <summary>
/// Example demonstrating how to use the RAG configuration system
/// </summary>
public class ConfigurationExample
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            // Create host builder
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Add RAG system with all services
                    services.AddRagSystem(configuration);
                    
                    // Add structured logging
                    services.AddRagLogging(configuration);
                    
                    // Add example service that uses RAG components
                    services.AddTransient<ExampleRagService>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .Build();

            // Get services
            var logger = host.Services.GetRequiredService<ILogger<ConfigurationExample>>();
            var exampleService = host.Services.GetRequiredService<ExampleRagService>();
            var shutdownService = host.Services.GetRequiredService<GracefulShutdownService>();

            logger.LogInformation("Starting RAG system example...");

            // Register shutdown callbacks
            shutdownService.RegisterShutdownCallback(async ct =>
            {
                logger.LogInformation("Executing custom shutdown logic...");
                await Task.Delay(1000, ct);
                logger.LogInformation("Custom shutdown logic completed");
            });

            // Run the example
            await exampleService.RunExampleAsync();

            // Start the host
            await host.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application failed to start: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Example service that demonstrates using RAG system services
/// </summary>
public class ExampleRagService
{
    private readonly ILogger<ExampleRagService> _logger;
    private readonly RetryPolicyService _retryService;
    private readonly PerformanceMonitoringService _performanceService;
    private readonly MemoryManagementService _memoryService;
    private readonly GracefulShutdownService _shutdownService;

    public ExampleRagService(
        ILogger<ExampleRagService> logger,
        RetryPolicyService retryService,
        PerformanceMonitoringService performanceService,
        MemoryManagementService memoryService,
        GracefulShutdownService shutdownService)
    {
        _logger = logger;
        _retryService = retryService;
        _performanceService = performanceService;
        _memoryService = memoryService;
        _shutdownService = shutdownService;
    }

    public async Task RunExampleAsync()
    {
        _logger.LogInformation("Running RAG system example...");

        // Example 1: Using retry policy
        await DemonstrateRetryPolicy();

        // Example 2: Using performance monitoring
        await DemonstratePerformanceMonitoring();

        // Example 3: Using memory management
        DemonstrateMemoryManagement();

        // Example 4: Using graceful shutdown
        DemonstrateGracefulShutdown();

        _logger.LogInformation("RAG system example completed successfully");
    }

    private async Task DemonstrateRetryPolicy()
    {
        _logger.LogInformation("=== Demonstrating Retry Policy ===");

        try
        {
            var result = await _retryService.ExecuteWithRetryAsync(() =>
            {
                _logger.LogInformation("Executing operation that might fail...");
                
                // Simulate occasional failure
                if (Random.Shared.NextDouble() < 0.3)
                {
                    throw new HttpRequestException("Simulated network error");
                }

                return Task.FromResult("Operation succeeded!");
            }, "SimulatedOperation");

            _logger.LogInformation("Operation result: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation failed after all retries");
        }
    }

    private async Task DemonstratePerformanceMonitoring()
    {
        _logger.LogInformation("=== Demonstrating Performance Monitoring ===");

        // Measure a simulated document processing operation
        var result = await _performanceService.MeasureAsync(async () =>
        {
            _logger.LogInformation("Processing document...");
            await Task.Delay(Random.Shared.Next(1000, 3000)); // Simulate processing time
            return "Document processed successfully";
        }, "DocumentProcessing");

        _logger.LogInformation("Processing result: {Result}", result);

        // Record custom metrics
        _performanceService.RecordMetric("DocumentSize", Random.Shared.Next(100, 1000));
        _performanceService.RecordMetric("ChunkCount", Random.Shared.Next(10, 50));

        // Get metrics snapshot
        var metrics = _performanceService.GetMetricsSnapshot();
        _logger.LogInformation("Current metrics count: {Count}", metrics.Count);
    }

    private void DemonstrateMemoryManagement()
    {
        _logger.LogInformation("=== Demonstrating Memory Management ===");

        // Register memory pressure callback
        _memoryService.RegisterMemoryPressureCallback(async () =>
        {
            _logger.LogWarning("Memory pressure detected, cleaning up caches...");
            await Task.Delay(100); // Simulate cleanup
        });

        // Cache some test data
        var testEmbedding = new float[384];
        Random.Shared.NextSingle(); // Fill with random data
        _memoryService.CacheEmbedding("test-key", testEmbedding);

        // Retrieve cached data
        var cachedEmbedding = _memoryService.GetCachedEmbedding("test-key");
        _logger.LogInformation("Cached embedding retrieved: {Found}", cachedEmbedding != null);

        // Get memory usage stats
        var memoryStats = _memoryService.GetMemoryUsage();
        _logger.LogInformation("Current memory usage: Working Set = {WorkingSet:F1} MB, Managed = {Managed:F1} MB",
            memoryStats.WorkingSetBytes / (1024.0 * 1024.0),
            memoryStats.ManagedMemoryBytes / (1024.0 * 1024.0));
    }

    private void DemonstrateGracefulShutdown()
    {
        _logger.LogInformation("=== Demonstrating Graceful Shutdown ===");

        // Register state provider
        _shutdownService.RegisterStateProvider(() =>
        {
            return Task.FromResult((object)new
            {
                ExampleState = "This is example state data",
                Timestamp = DateTimeOffset.UtcNow,
                ProcessedDocuments = Random.Shared.Next(1, 100)
            });
        });

        // Simulate a long-running operation
        var taskId = Guid.NewGuid().ToString();
        _ = Task.Run(async () =>
        {
            await _shutdownService.ExecuteTaskAsync(taskId, "Long running background task", async ct =>
            {
                for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
                {
                    _logger.LogDebug("Background task iteration {Iteration}", i + 1);
                    await Task.Delay(1000, ct);
                }
            });
        });

        // Get active tasks
        var activeTasks = _shutdownService.GetActiveTasks();
        _logger.LogInformation("Currently active tasks: {Count}", activeTasks.Count);
    }
}

/// <summary>
/// Example of environment-specific configuration
/// </summary>
public static class EnvironmentConfigurationExample
{
    public static void ConfigureForEnvironment(IServiceCollection services, IConfiguration configuration, string environment)
    {
        switch (environment.ToLowerInvariant())
        {
            case "development":
                services.PostConfigure<RagConfiguration>(options =>
                {
                    options.Logging.MinimumLevel = LogLevel.Debug;
                    options.Performance.EnableTracing = true;
                    options.Memory.EnableMemoryPressureCallback = true;
                });
                break;

            case "staging":
                services.PostConfigure<RagConfiguration>(options =>
                {
                    options.Logging.MinimumLevel = LogLevel.Information;
                    options.Performance.EnableMetrics = true;
                    options.RetryPolicy.MaxRetries = 5;
                });
                break;

            case "production":
                services.PostConfigure<RagConfiguration>(options =>
                {
                    options.Logging.MinimumLevel = LogLevel.Warning;
                    options.Logging.LogSensitiveData = false;
                    options.Performance.LogSlowOperations = true;
                    options.Memory.EnableGarbageCollectionTuning = true;
                });
                break;
        }
    }
} 