using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Ragtut.Core.Models;

/// <summary>
/// Root configuration class for the RAG system
/// </summary>
public class RagConfiguration
{
    public const string SectionName = "RagSystem";

    [Required]
    public DocumentProcessingConfig DocumentProcessing { get; set; } = new();

    [Required]
    public EmbeddingModelConfig EmbeddingModel { get; set; } = new();

    [Required]
    public VectorStoreConfig VectorStore { get; set; } = new();

    [Required]
    public LlmConfig Llm { get; set; } = new();

    [Required]
    public RagConfig Rag { get; set; } = new();

    public RetryPolicyConfig RetryPolicy { get; set; } = new();

    public PerformanceConfig Performance { get; set; } = new();

    public MemoryConfig Memory { get; set; } = new();

    public ShutdownConfig Shutdown { get; set; } = new();

    public LoggingConfig Logging { get; set; } = new();
}

/// <summary>
/// Document processing configuration
/// </summary>
public class DocumentProcessingConfig
{
    [Range(100, 2000)]
    public int ChunkSize { get; set; } = 800;

    [Range(0, 500)]
    public int ChunkOverlap { get; set; } = 100;

    [Required]
    public string[] SupportedExtensions { get; set; } = { ".pdf", ".txt", ".docx", ".md" };

    [Range(1, 100)]
    public int MaxConcurrentProcessing { get; set; } = 4;

    [Range(1, 1000)]
    public int MaxDocumentSizeMB { get; set; } = 100;

    public bool EnableOcr { get; set; } = false;

    public string TempDirectory { get; set; } = Path.GetTempPath();
}

/// <summary>
/// Embedding model configuration
/// </summary>
public class EmbeddingModelConfig
{
    [Required]
    public string ModelPath { get; set; } = string.Empty;

    [Range(1, 4096)]
    public int Dimension { get; set; } = 384;

    [Range(1, 1024)]
    public int MaxSequenceLength { get; set; } = 512;

    public bool UseGpu { get; set; } = false;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 32;

    public string Provider { get; set; } = "ONNX"; // ONNX, HuggingFace, OpenAI, etc.
}

/// <summary>
/// Vector store configuration
/// </summary>
public class VectorStoreConfig
{
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string Provider { get; set; } = "SQLite"; // SQLite, PostgreSQL, Pinecone, Weaviate, etc.

    public string DatabasePath { get; set; } = "data/vectors.db";

    [Range(1, 10000)]
    public int QueryBatchSize { get; set; } = 100;

    [Range(1, 1000)]
    public int IndexBatchSize { get; set; } = 50;

    public bool EnableWalMode { get; set; } = true;

    [Range(1, 3600)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    public Dictionary<string, string> ProviderSettings { get; set; } = new();
}

/// <summary>
/// LLM API configuration
/// </summary>
public class LlmConfig
{
    [Required]
    public string BaseUrl { get; set; } = "http://localhost:11434";

    [Required]
    public string Model { get; set; } = "llama2";

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.7;

    [Range(1, 8192)]
    public int MaxTokens { get; set; } = 1024;

    [Range(0.0, 1.0)]
    public double TopP { get; set; } = 0.9;

    [Range(1, 100)]
    public int TopK { get; set; } = 40;

    [Range(0.0, 2.0)]
    public double RepetitionPenalty { get; set; } = 1.1;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 60;

    public string ApiKey { get; set; } = string.Empty;

    public Dictionary<string, object> AdditionalParameters { get; set; } = new();

    public string Provider { get; set; } = "Ollama"; // Ollama, OpenAI, Anthropic, etc.
}

/// <summary>
/// RAG-specific configuration
/// </summary>
public class RagConfig
{
    [Range(1, 20)]
    public int MaxChunks { get; set; } = 5;

    [Range(0.0, 1.0)]
    public double SimilarityThreshold { get; set; } = 0.7;

    public bool EnableReranking { get; set; } = false;

    public string SystemPrompt { get; set; } = "You are a helpful assistant that answers questions based on the provided context.";

    [Range(1, 10)]
    public int MaxConcurrentQueries { get; set; } = 3;

    public bool EnableCaching { get; set; } = true;

    [Range(1, 3600)]
    public int CacheTtlSeconds { get; set; } = 300;
}

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryPolicyConfig
{
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(100, 30000)]
    public int BaseDelayMs { get; set; } = 1000;

    [Range(1.1, 5.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    [Range(1000, 60000)]
    public int MaxDelayMs { get; set; } = 10000;

    public bool EnableJitter { get; set; } = true;

    public string[] RetriableExceptions { get; set; } = 
    {
        "HttpRequestException",
        "TaskCanceledException",
        "SqliteException"
    };
}

/// <summary>
/// Performance monitoring configuration
/// </summary>
public class PerformanceConfig
{
    public bool EnableMetrics { get; set; } = true;

    public bool EnableTracing { get; set; } = false;

    [Range(1, 3600)]
    public int MetricsIntervalSeconds { get; set; } = 60;

    public bool LogSlowOperations { get; set; } = true;

    [Range(100, 60000)]
    public int SlowOperationThresholdMs { get; set; } = 5000;

    public string[] TrackedOperations { get; set; } = 
    {
        "DocumentIndexing",
        "VectorSearch", 
        "LlmInference",
        "EmbeddingGeneration"
    };
}

/// <summary>
/// Memory management configuration
/// </summary>
public class MemoryConfig
{
    [Range(100, 10000)]
    public int MaxDocumentCacheSizeMB { get; set; } = 500;

    [Range(50, 2000)]
    public int MaxEmbeddingCacheSizeMB { get; set; } = 200;

    public bool EnableGarbageCollectionTuning { get; set; } = true;

    [Range(1, 100)]
    public int GcPressureThresholdMB { get; set; } = 50;

    public bool EnableMemoryPressureCallback { get; set; } = true;

    [Range(10, 90)]
    public int MemoryWarningThresholdPercent { get; set; } = 80;
}

/// <summary>
/// Graceful shutdown configuration
/// </summary>
public class ShutdownConfig
{
    [Range(1, 60)]
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;

    public bool WaitForActiveOperations { get; set; } = true;

    public bool SaveStateOnShutdown { get; set; } = true;

    public string StateFilePath { get; set; } = "data/shutdown-state.json";

    public bool EnableShutdownMetrics { get; set; } = true;
}

/// <summary>
/// Logging configuration
/// </summary>
public class LoggingConfig
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public bool EnableConsoleLogging { get; set; } = true;

    public bool EnableFileLogging { get; set; } = true;

    public string LogFilePath { get; set; } = "logs/ragtut-{Date}.log";

    public bool EnableStructuredLogging { get; set; } = true;

    public bool LogSensitiveData { get; set; } = false;

    [Range(1, 1000)]
    public int MaxLogFileSizeMB { get; set; } = 100;

    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    public Dictionary<string, LogLevel> CategoryOverrides { get; set; } = new();
} 