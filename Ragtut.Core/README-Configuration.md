# RAG System Configuration Guide

This guide explains the comprehensive configuration system for the RAG (Retrieval-Augmented Generation) application, including all features, services, and best practices.

## Overview

The RAG configuration system provides:

- **Centralized Configuration**: Single configuration model for all RAG components
- **Environment-based Settings**: Support for development, staging, and production environments
- **Retry Logic**: Configurable retry policies with exponential backoff
- **Performance Monitoring**: Built-in metrics and timing for all operations
- **Memory Management**: Intelligent caching and memory pressure handling
- **Graceful Shutdown**: Proper cleanup and state preservation on shutdown
- **Structured Logging**: Comprehensive logging with configurable levels

## Quick Start

### 1. Add RAG System to Your Application

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ragtut.Core.Extensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Add all RAG services with configuration
        services.AddRagSystem(context.Configuration);
        
        // Add structured logging
        services.AddRagLogging(context.Configuration);
        
        // Validate configuration at startup
        services.ValidateRagConfiguration();
    })
    .Build();

await host.RunAsync();
```

### 2. Configure appsettings.json

```json
{
  "RagSystem": {
    "DocumentProcessing": {
      "ChunkSize": 800,
      "ChunkOverlap": 100,
      "SupportedExtensions": [".pdf", ".txt", ".docx", ".md"]
    },
    "EmbeddingModel": {
      "ModelPath": "models/all-MiniLM-L6-v2.onnx",
      "Dimension": 384
    },
    "VectorStore": {
      "Provider": "SQLite",
      "DatabasePath": "data/vectors.db"
    },
    "Llm": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama2",
      "Temperature": 0.7
    }
  }
}
```

## Configuration Sections

### Document Processing (`DocumentProcessing`)

Controls how documents are processed and chunked:

```json
{
  "DocumentProcessing": {
    "ChunkSize": 800,                     // Size of text chunks in characters
    "ChunkOverlap": 100,                  // Overlap between chunks in characters
    "SupportedExtensions": [".pdf", ".txt", ".docx", ".md"],
    "MaxConcurrentProcessing": 4,         // Max parallel document processing
    "MaxDocumentSizeMB": 100,            // Max document size in MB
    "EnableOcr": false,                   // Enable OCR for image-based PDFs
    "TempDirectory": "temp"               // Temporary directory for processing
  }
}
```

### Embedding Model (`EmbeddingModel`)

Configuration for text embedding generation:

```json
{
  "EmbeddingModel": {
    "ModelPath": "models/all-MiniLM-L6-v2.onnx",  // Path to ONNX model
    "Dimension": 384,                              // Embedding vector dimension
    "MaxSequenceLength": 512,                     // Max input token length
    "UseGpu": false,                              // Enable GPU acceleration
    "BatchSize": 32,                              // Batch size for processing
    "Provider": "ONNX"                            // Provider: ONNX, HuggingFace, OpenAI
  }
}
```

### Vector Store (`VectorStore`)

Configuration for the vector database:

```json
{
  "VectorStore": {
    "ConnectionString": "",                       // Database connection string
    "Provider": "SQLite",                        // Provider: SQLite, PostgreSQL, Pinecone
    "DatabasePath": "data/vectors.db",           // SQLite database path
    "QueryBatchSize": 100,                       // Batch size for queries
    "IndexBatchSize": 50,                        // Batch size for indexing
    "EnableWalMode": true,                       // Enable WAL mode for SQLite
    "ConnectionTimeoutSeconds": 30,              // Connection timeout
    "ProviderSettings": {}                       // Provider-specific settings
  }
}
```

### LLM Configuration (`Llm`)

Settings for the Large Language Model:

```json
{
  "Llm": {
    "BaseUrl": "http://localhost:11434",         // LLM API endpoint
    "Model": "llama2",                           // Model name
    "Temperature": 0.7,                          // Response creativity (0-2)
    "MaxTokens": 1024,                          // Maximum response tokens
    "TopP": 0.9,                                // Nucleus sampling parameter
    "TopK": 40,                                 // Top-K sampling parameter
    "RepetitionPenalty": 1.1,                   // Repetition penalty
    "TimeoutSeconds": 60,                       // Request timeout
    "ApiKey": "",                               // API key if required
    "Provider": "Ollama"                        // Provider: Ollama, OpenAI, Anthropic
  }
}
```

### RAG Settings (`Rag`)

Core RAG functionality configuration:

```json
{
  "Rag": {
    "MaxChunks": 5,                             // Max chunks to retrieve
    "SimilarityThreshold": 0.7,                 // Minimum similarity score
    "EnableReranking": false,                   // Enable result reranking
    "SystemPrompt": "You are a helpful assistant...",
    "MaxConcurrentQueries": 3,                  // Max parallel queries
    "EnableCaching": true,                      // Enable response caching
    "CacheTtlSeconds": 300                      // Cache time-to-live
  }
}
```

### Retry Policy (`RetryPolicy`)

Configures retry behavior for failed operations:

```json
{
  "RetryPolicy": {
    "MaxRetries": 3,                            // Maximum retry attempts
    "BaseDelayMs": 1000,                        // Base delay in milliseconds
    "BackoffMultiplier": 2.0,                   // Exponential backoff multiplier
    "MaxDelayMs": 10000,                        // Maximum delay between retries
    "EnableJitter": true,                       // Add random jitter to delays
    "RetriableExceptions": [                    // Exception types to retry
      "HttpRequestException",
      "TaskCanceledException",
      "SqliteException"
    ]
  }
}
```

### Performance Monitoring (`Performance`)

Settings for performance tracking and metrics:

```json
{
  "Performance": {
    "EnableMetrics": true,                      // Enable performance metrics
    "EnableTracing": false,                     // Enable detailed tracing
    "MetricsIntervalSeconds": 60,               // Metrics reporting interval
    "LogSlowOperations": true,                  // Log slow operations
    "SlowOperationThresholdMs": 5000,           // Slow operation threshold
    "TrackedOperations": [                      // Operations to track
      "DocumentIndexing",
      "VectorSearch",
      "LlmInference",
      "EmbeddingGeneration"
    ]
  }
}
```

### Memory Management (`Memory`)

Memory usage and garbage collection settings:

```json
{
  "Memory": {
    "MaxDocumentCacheSizeMB": 500,              // Max document cache size
    "MaxEmbeddingCacheSizeMB": 200,             // Max embedding cache size
    "EnableGarbageCollectionTuning": true,      // Enable GC optimization
    "GcPressureThresholdMB": 50,                // GC pressure threshold
    "EnableMemoryPressureCallback": true,       // Enable memory callbacks
    "MemoryWarningThresholdPercent": 80         // Memory warning threshold
  }
}
```

### Graceful Shutdown (`Shutdown`)

Configuration for application shutdown behavior:

```json
{
  "Shutdown": {
    "GracefulShutdownTimeoutSeconds": 30,       // Max shutdown wait time
    "WaitForActiveOperations": true,            // Wait for active operations
    "SaveStateOnShutdown": true,                // Save state on shutdown
    "StateFilePath": "data/shutdown-state.json", // State file path
    "EnableShutdownMetrics": true               // Log shutdown metrics
  }
}
```

### Logging (`Logging`)

Comprehensive logging configuration:

```json
{
  "Logging": {
    "MinimumLevel": "Information",              // Minimum log level
    "EnableConsoleLogging": true,               // Enable console output
    "EnableFileLogging": true,                  // Enable file logging
    "LogFilePath": "logs/ragtut-{Date}.log",   // Log file path pattern
    "EnableStructuredLogging": true,            // Enable structured logging
    "LogSensitiveData": false,                  // Log sensitive information
    "MaxLogFileSizeMB": 100,                   // Max log file size
    "RetentionDays": 30,                       // Log retention period
    "CategoryOverrides": {                      // Category-specific levels
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
```

## Usage Examples

### Using Retry Policy

```csharp
public class DocumentService
{
    private readonly RetryPolicyService _retryService;

    public DocumentService(RetryPolicyService retryService)
    {
        _retryService = retryService;
    }

    public async Task<string> ProcessDocumentAsync(string documentPath)
    {
        return await _retryService.ExecuteWithRetryAsync(async () =>
        {
            // Operation that might fail
            return await ActualDocumentProcessing(documentPath);
        }, "DocumentProcessing");
    }
}
```

### Using Performance Monitoring

```csharp
public class EmbeddingService
{
    private readonly PerformanceMonitoringService _performanceService;

    public EmbeddingService(PerformanceMonitoringService performanceService)
    {
        _performanceService = performanceService;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        return await _performanceService.MeasureAsync(async () =>
        {
            // Measured operation
            return await ActualEmbeddingGeneration(text);
        }, "EmbeddingGeneration");
    }
}
```

### Using Memory Management

```csharp
public class CacheService
{
    private readonly MemoryManagementService _memoryService;

    public CacheService(MemoryManagementService memoryService)
    {
        _memoryService = memoryService;
        
        // Register memory pressure callback
        _memoryService.RegisterMemoryPressureCallback(async () =>
        {
            await ClearOldCacheEntries();
        });
    }

    public void CacheEmbedding(string key, float[] embedding)
    {
        _memoryService.CacheEmbedding(key, embedding);
    }
}
```

### Using Graceful Shutdown

```csharp
public class BackgroundService
{
    private readonly GracefulShutdownService _shutdownService;

    public BackgroundService(GracefulShutdownService shutdownService)
    {
        _shutdownService = shutdownService;
    }

    public async Task RunLongOperationAsync()
    {
        var taskId = Guid.NewGuid().ToString();
        
        await _shutdownService.ExecuteTaskAsync(taskId, "Background processing", async ct =>
        {
            // Long-running operation that respects cancellation
            while (!ct.IsCancellationRequested)
            {
                await ProcessNextItem(ct);
            }
        });
    }
}
```

## Environment-Specific Configuration

### Development Environment

```csharp
services.PostConfigure<RagConfiguration>(options =>
{
    options.Logging.MinimumLevel = LogLevel.Debug;
    options.Performance.EnableTracing = true;
    options.Memory.EnableMemoryPressureCallback = true;
});
```

### Production Environment

```csharp
services.PostConfigure<RagConfiguration>(options =>
{
    options.Logging.MinimumLevel = LogLevel.Warning;
    options.Logging.LogSensitiveData = false;
    options.Performance.LogSlowOperations = true;
    options.Memory.EnableGarbageCollectionTuning = true;
});
```

## Environment Variables

Override configuration using environment variables:

```bash
# Override LLM settings
export RagSystem__Llm__BaseUrl="http://production-llm:11434"
export RagSystem__Llm__Model="llama2-production"

# Override logging level
export RagSystem__Logging__MinimumLevel="Warning"

# Override memory settings
export RagSystem__Memory__MaxDocumentCacheSizeMB="1000"
```

## Best Practices

### 1. Configuration Validation

Always validate configuration at startup:

```csharp
services.ValidateRagConfiguration();
```

### 2. Environment-Specific Settings

Use separate configuration files for different environments:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production overrides

### 3. Security

- Never store sensitive data (API keys, passwords) in configuration files
- Use environment variables or secure configuration providers for secrets
- Set `LogSensitiveData` to `false` in production

### 4. Performance

- Tune `ChunkSize` and `ChunkOverlap` based on your content
- Adjust `BatchSize` for embedding generation based on available memory
- Configure appropriate cache sizes for your workload

### 5. Monitoring

- Enable performance monitoring in production
- Set appropriate thresholds for slow operation detection
- Configure memory pressure callbacks for large-scale deployments

### 6. Backup and Recovery

- Enable state saving during shutdown for critical applications
- Regularly backup vector databases
- Monitor disk space for log files

## Troubleshooting

### Common Issues

1. **Configuration Validation Errors**
   - Check that all required fields are properly set
   - Verify file paths exist and are accessible
   - Ensure URL formats are valid

2. **Memory Issues**
   - Reduce cache sizes if experiencing memory pressure
   - Enable garbage collection tuning
   - Monitor memory usage patterns

3. **Performance Problems**
   - Enable performance monitoring to identify bottlenecks
   - Adjust batch sizes for your hardware
   - Consider enabling GPU acceleration for embeddings

4. **Connection Issues**
   - Verify LLM endpoint is accessible
   - Check network connectivity and timeouts
   - Review retry policy settings

For more examples and advanced usage, see the `Examples/ConfigurationExample.cs` file in the Ragtut.Core project. 