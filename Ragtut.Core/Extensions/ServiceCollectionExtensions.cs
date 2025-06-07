using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ragtut.Core.Models;
using Ragtut.Core.Services;
using System.ComponentModel.DataAnnotations;

namespace Ragtut.Core.Extensions;

/// <summary>
/// Extension methods for IServiceCollection to register RAG system services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all RAG system services and configuration
    /// </summary>
    public static IServiceCollection AddRagSystem(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSection = RagConfiguration.SectionName)
    {
        // Register and validate configuration
        services.Configure<RagConfiguration>(configuration.GetSection(configurationSection));
        services.AddSingleton<IValidateOptions<RagConfiguration>, RagConfigurationValidator>();

        // Register core services
        services.AddSingleton<RetryPolicyService>();
        services.AddSingleton<PerformanceMonitoringService>();
        services.AddSingleton<MemoryManagementService>();
        services.AddHostedService<GracefulShutdownService>();
        services.AddSingleton<GracefulShutdownService>(provider => 
            provider.GetServices<IHostedService>()
                    .OfType<GracefulShutdownService>()
                    .First());

        // Register existing services
        services.AddTransient<DocumentProcessor>();
        services.AddSingleton<SqliteVectorStore>();
        services.AddSingleton<EmbeddingGenerator>();

        return services;
    }

    /// <summary>
    /// Adds RAG system services with custom configuration validation
    /// </summary>
    public static IServiceCollection AddRagSystem<TValidator>(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSection = RagConfiguration.SectionName)
        where TValidator : class, IValidateOptions<RagConfiguration>
    {
        services.Configure<RagConfiguration>(configuration.GetSection(configurationSection));
        services.AddSingleton<IValidateOptions<RagConfiguration>, TValidator>();

        services.AddSingleton<RetryPolicyService>();
        services.AddSingleton<PerformanceMonitoringService>();
        services.AddSingleton<MemoryManagementService>();
        services.AddHostedService<GracefulShutdownService>();
        services.AddSingleton<GracefulShutdownService>(provider => 
            provider.GetServices<IHostedService>()
                    .OfType<GracefulShutdownService>()
                    .First());

        services.AddTransient<DocumentProcessor>();
        services.AddSingleton<SqliteVectorStore>();
        services.AddSingleton<EmbeddingGenerator>();

        return services;
    }

    /// <summary>
    /// Adds structured logging configuration
    /// </summary>
    public static IServiceCollection AddRagLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddLogging(builder =>
        {
            var loggingConfig = new LoggingConfig();
            configuration.GetSection($"{RagConfiguration.SectionName}:Logging").Bind(loggingConfig);

            builder.ClearProviders();

            if (loggingConfig.EnableConsoleLogging)
            {
                builder.AddConsole(options =>
                {
                    // Note: ConsoleLoggerOptions.IncludeScopes is deprecated, use ConsoleFormatterOptions.IncludeScopes instead
                    #pragma warning disable CS0618
                    options.IncludeScopes = loggingConfig.EnableStructuredLogging;
                    #pragma warning restore CS0618
                });
            }

            if (loggingConfig.EnableFileLogging)
            {
                // Note: You might want to add Serilog or NLog here for file logging
                // This is a placeholder for file logging configuration
            }

            builder.SetMinimumLevel(loggingConfig.MinimumLevel);

            // Apply category-specific log levels
            foreach (var categoryOverride in loggingConfig.CategoryOverrides)
            {
                builder.AddFilter(categoryOverride.Key, categoryOverride.Value);
            }
        });

        return services;
    }

    /// <summary>
    /// Validates the RAG configuration and throws if invalid
    /// </summary>
    public static IServiceCollection ValidateRagConfiguration(this IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<RagConfiguration>>();
        var validator = serviceProvider.GetService<IValidateOptions<RagConfiguration>>();

        if (validator != null)
        {
            var result = validator.Validate(string.Empty, options.Value);
            if (result.Failed)
            {
                throw new InvalidOperationException($"RAG configuration validation failed: {string.Join(", ", result.Failures)}");
            }
        }

        return services;
    }

    /// <summary>
    /// Adds memory management with custom callbacks
    /// </summary>
    public static IServiceCollection AddMemoryManagement(
        this IServiceCollection services,
        Action<MemoryManagementService> configure)
    {
        services.PostConfigure<MemoryManagementService>(configure);
        return services;
    }

    /// <summary>
    /// Adds performance monitoring with custom configuration
    /// </summary>
    public static IServiceCollection AddPerformanceMonitoring(
        this IServiceCollection services,
        Action<PerformanceMonitoringService> configure)
    {
        services.PostConfigure<PerformanceMonitoringService>(configure);
        return services;
    }
}

/// <summary>
/// Validator for RAG configuration
/// </summary>
public class RagConfigurationValidator : IValidateOptions<RagConfiguration>
{
    public ValidateOptionsResult Validate(string? name, RagConfiguration options)
    {
        var failures = new List<string>();

        // Validate using data annotations
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(options);
        
        if (!Validator.TryValidateObject(options, validationContext, validationResults, true))
        {
            failures.AddRange(validationResults.Select(r => r.ErrorMessage ?? "Unknown validation error"));
        }

        // Custom validation logic
        ValidateDocumentProcessing(options.DocumentProcessing, failures);
        ValidateEmbeddingModel(options.EmbeddingModel, failures);
        ValidateVectorStore(options.VectorStore, failures);
        ValidateLlm(options.Llm, failures);
        ValidateRetryPolicy(options.RetryPolicy, failures);

        return failures.Count > 0 
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private void ValidateDocumentProcessing(DocumentProcessingConfig config, List<string> failures)
    {
        if (config.ChunkOverlap >= config.ChunkSize)
        {
            failures.Add("Chunk overlap must be less than chunk size");
        }

        if (config.SupportedExtensions.Length == 0)
        {
            failures.Add("At least one supported file extension must be specified");
        }

        if (!Directory.Exists(config.TempDirectory))
        {
            try
            {
                Directory.CreateDirectory(config.TempDirectory);
            }
            catch
            {
                failures.Add($"Cannot create or access temp directory: {config.TempDirectory}");
            }
        }
    }

    private void ValidateEmbeddingModel(EmbeddingModelConfig config, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(config.ModelPath))
        {
            failures.Add("Embedding model path is required");
        }
        else if (!File.Exists(config.ModelPath) && !config.ModelPath.StartsWith("http"))
        {
            failures.Add($"Embedding model file not found: {config.ModelPath}");
        }

        if (config.Dimension <= 0)
        {
            failures.Add("Embedding dimension must be positive");
        }
    }

    private void ValidateVectorStore(VectorStoreConfig config, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString) && string.IsNullOrWhiteSpace(config.DatabasePath))
        {
            failures.Add("Either connection string or database path must be specified");
        }

        if (config.Provider == "SQLite" && !string.IsNullOrWhiteSpace(config.DatabasePath))
        {
            var directory = Path.GetDirectoryName(config.DatabasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch
                {
                    failures.Add($"Cannot create database directory: {directory}");
                }
            }
        }
    }

    private void ValidateLlm(LlmConfig config, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            failures.Add("LLM base URL is required");
        }
        else if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add("LLM base URL must be a valid URL");
        }

        if (string.IsNullOrWhiteSpace(config.Model))
        {
            failures.Add("LLM model name is required");
        }

        if (config.Temperature < 0 || config.Temperature > 2)
        {
            failures.Add("LLM temperature must be between 0 and 2");
        }
    }

    private void ValidateRetryPolicy(RetryPolicyConfig config, List<string> failures)
    {
        if (config.MaxRetries <= 0)
        {
            failures.Add("Max retries must be positive");
        }

        if (config.BaseDelayMs <= 0)
        {
            failures.Add("Base delay must be positive");
        }

        if (config.BackoffMultiplier <= 1)
        {
            failures.Add("Backoff multiplier must be greater than 1");
        }

        if (config.MaxDelayMs < config.BaseDelayMs)
        {
            failures.Add("Max delay must be greater than or equal to base delay");
        }
    }
} 