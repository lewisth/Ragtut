using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Services;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Models;

namespace Ragtut.Tests.Examples;

/// <summary>
/// Example demonstrating how to use the quality evaluation and optimization services
/// </summary>
public class QualityEvaluationExample
{
    public static async Task RunExampleAsync()
    {
        // Setup services
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        // Get required services
        var documentProcessor = serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = serviceProvider.GetRequiredService<IVectorStore>();
        var qualityService = serviceProvider.GetRequiredService<QualityEvaluationService>();
        var optimizationService = serviceProvider.GetRequiredService<OptimizationService>();

        // Initialize vector store
        await vectorStore.InitializeAsync();

        // Example 1: Process documents and evaluate chunk quality
        Console.WriteLine("=== Chunk Quality Evaluation ===");
        await EvaluateChunkQualityExample(documentProcessor, qualityService);

        // Example 2: Test retrieval performance
        Console.WriteLine("\n=== Retrieval Performance Evaluation ===");
        await EvaluateRetrievalPerformanceExample(vectorStore, qualityService);

        // Example 3: Optimize chunk sizes
        Console.WriteLine("\n=== Chunk Size Optimization ===");
        await OptimizeChunkSizeExample(optimizationService);

        // Example 4: Test caching performance
        Console.WriteLine("\n=== Caching Performance ===");
        await TestCachingPerformanceExample(optimizationService);

        // Example 5: Batch processing optimization
        Console.WriteLine("\n=== Batch Processing ===");
        await BatchProcessingExample(optimizationService);

        // Example 6: Generate comprehensive quality report
        Console.WriteLine("\n=== Comprehensive Quality Report ===");
        await GenerateQualityReportExample(vectorStore, qualityService);
    }

    private static async Task EvaluateChunkQualityExample(
        IDocumentProcessor documentProcessor,
        QualityEvaluationService qualityService)
    {
        // Create a test document
        var testDocPath = CreateTestDocument();

        try
        {
            // Process document
            var chunks = await documentProcessor.ProcessDocumentAsync(testDocPath);
            Console.WriteLine($"Processed document into {chunks.Count()} chunks");

            // Evaluate chunk quality
            var chunkQuality = qualityService.EvaluateChunkQuality(chunks);
            
            Console.WriteLine($"Average chunk length: {chunkQuality.AverageTextLength:F1} characters");
            Console.WriteLine($"Total unique documents: {chunkQuality.UniqueDocuments}");
            Console.WriteLine($"Duplicate chunks: {chunkQuality.DuplicateHashCount}");
            Console.WriteLine($"Embedding coherence score: {chunkQuality.EmbeddingCoherenceScore:F3}");
        }
        finally
        {
            if (File.Exists(testDocPath))
                File.Delete(testDocPath);
        }
    }

    private static async Task EvaluateRetrievalPerformanceExample(
        IVectorStore vectorStore,
        QualityEvaluationService qualityService)
    {
        // Create test cases
        var testCases = CreateTestCases();

        // Evaluate retrieval performance
        var retrievalPerformance = await qualityService.EvaluateRetrievalPerformanceAsync(vectorStore, testCases);
        
        Console.WriteLine($"Tested {retrievalPerformance.TotalTestCases} queries");
        Console.WriteLine($"Average retrieval time: {retrievalPerformance.AverageRetrievalTime.TotalMilliseconds:F2} ms");
        Console.WriteLine($"Average F1 score: {retrievalPerformance.AverageF1Score:F3}");
        Console.WriteLine($"Average similarity: {retrievalPerformance.AverageSimilarity:F3}");
    }

    private static async Task OptimizeChunkSizeExample(OptimizationService optimizationService)
    {
        var testDocPath = CreateTestDocument();
        var testQueries = CreateTestCases();

        try
        {
            var optimization = await optimizationService.OptimizeChunkSizeAsync(
                testDocPath, "text", testQueries);
            
            Console.WriteLine($"Original chunk size: {optimization.OriginalChunkSize}");
            Console.WriteLine($"Optimal chunk size: {optimization.OptimalChunkSize}");
            Console.WriteLine($"Performance improvement: {optimization.PerformanceImprovement:F2}%");
            
            foreach (var result in optimization.TestResults.Take(3))
            {
                Console.WriteLine($"  Size {result.ChunkSize}: Score {result.AverageRelevanceScore:F3}, " +
                                $"Time {result.ProcessingTime.TotalMilliseconds:F0}ms");
            }
        }
        finally
        {
            if (File.Exists(testDocPath))
                File.Delete(testDocPath);
        }
    }

    private static async Task TestCachingPerformanceExample(OptimizationService optimizationService)
    {
        var testTexts = new[]
        {
            "Machine learning is a subset of artificial intelligence",
            "Deep learning uses neural networks with multiple layers",
            "Natural language processing enables computers to understand human language",
            "Machine learning is a subset of artificial intelligence", // Duplicate for cache hit
            "Computer vision allows machines to interpret visual information"
        };

        Console.WriteLine("Testing embedding caching...");
        
        // First pass - populate cache
        foreach (var text in testTexts)
        {
            await optimizationService.GetCachedEmbeddingAsync(text);
        }

        // Second pass - should hit cache for duplicates
        foreach (var text in testTexts)
        {
            await optimizationService.GetCachedEmbeddingAsync(text);
        }

        var cacheMetrics = optimizationService.GetCachePerformanceMetrics();
        Console.WriteLine($"Cache hit rate: {cacheMetrics.HitRate:P1}");
        Console.WriteLine($"Performance gain: {cacheMetrics.PerformanceGain:F1}x");
        Console.WriteLine($"Cache size: {cacheMetrics.CacheSizeBytes / 1024:F1} KB");
    }

    private static async Task BatchProcessingExample(OptimizationService optimizationService)
    {
        // Create multiple test documents
        var documentPaths = new List<string>();
        
        for (int i = 0; i < 5; i++)
        {
            var path = CreateTestDocument($"Test document {i} content");
            documentPaths.Add(path);
        }

        try
        {
            var batchMetrics = await optimizationService.ProcessDocumentBatchAsync(documentPaths, batchSize: 2);
            
            Console.WriteLine($"Processed {batchMetrics.TotalDocuments} documents");
            Console.WriteLine($"Processing rate: {batchMetrics.DocumentsPerSecond:F2} docs/sec");
            Console.WriteLine($"Success rate: {batchMetrics.SuccessRate:P1}");
            Console.WriteLine($"Total chunks generated: {batchMetrics.TotalChunksGenerated}");
        }
        finally
        {
            foreach (var path in documentPaths)
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static async Task GenerateQualityReportExample(
        IVectorStore vectorStore,
        QualityEvaluationService qualityService)
    {
        var testCases = CreateTestCases();
        var chunks = CreateSampleChunks();

        var report = await qualityService.GenerateQualityReportAsync(vectorStore, testCases, chunks);
        
        Console.WriteLine($"Overall Quality Score: {report.OverallScore:F1}/10");
        Console.WriteLine("\nRecommendations:");
        foreach (var recommendation in report.Recommendations)
        {
            Console.WriteLine($"  - {recommendation}");
        }

        // Export report
        var reportPath = Path.Combine(Path.GetTempPath(), "quality_report.json");
        await qualityService.ExportMetricsAsync(report, reportPath);
        Console.WriteLine($"Report exported to: {reportPath}");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessing:SupportedExtensions:0"] = ".txt",
                ["DocumentProcessing:ChunkSize"] = "1000",
                ["DocumentProcessing:ChunkOverlap"] = "100"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole());
        
        // Mock services for example
        services.AddSingleton<IEmbeddingGenerator, MockEmbeddingGenerator>();
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();
        services.AddSingleton<IVectorStore>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<SqliteVectorStore>>();
            var dbPath = Path.Combine(Path.GetTempPath(), "example_db.db");
            return new SqliteVectorStore(dbPath, logger);
        });
        
        services.AddSingleton<QualityEvaluationService>();
        services.AddSingleton<OptimizationService>();
    }

    private static string CreateTestDocument(string content = "")
    {
        var defaultContent = @"
# Machine Learning Overview

Machine learning is a powerful subset of artificial intelligence that enables computers to learn and improve from experience without being explicitly programmed.

## Types of Machine Learning

### Supervised Learning
Supervised learning algorithms learn from labeled training data to make predictions on new, unseen data. Common examples include:
- Linear regression for numerical predictions
- Classification algorithms for categorical predictions
- Decision trees for interpretable models

### Unsupervised Learning
Unsupervised learning finds patterns in data without labeled examples:
- Clustering algorithms group similar data points
- Dimensionality reduction techniques compress data while preserving important features
- Association rules discover relationships between variables

### Reinforcement Learning
Reinforcement learning trains agents to make optimal decisions through trial and error:
- The agent receives rewards or penalties for actions
- Over time, it learns to maximize cumulative reward
- Applications include game playing, robotics, and autonomous systems

## Applications
Machine learning has revolutionized many industries:
- Healthcare: Medical image analysis, drug discovery, personalized treatment
- Finance: Fraud detection, algorithmic trading, credit scoring
- Technology: Search engines, recommendation systems, natural language processing
- Transportation: Autonomous vehicles, route optimization, predictive maintenance

## Conclusion
The field continues to evolve rapidly with new algorithms, techniques, and applications emerging constantly.
        ";

        var filePath = Path.GetTempFileName();
        File.WriteAllText(filePath, string.IsNullOrEmpty(content) ? defaultContent : content);
        return filePath;
    }

    private static List<QueryTestCase> CreateTestCases()
    {
        return new List<QueryTestCase>
        {
            new()
            {
                Query = "What is supervised learning?",
                Category = "Machine Learning",
                Description = "Basic concept query about supervised learning"
            },
            new()
            {
                Query = "How does reinforcement learning work?",
                Category = "Machine Learning",
                Description = "Technical query about reinforcement learning mechanism"
            },
            new()
            {
                Query = "Applications of machine learning in healthcare",
                Category = "Applications",
                Description = "Domain-specific application query"
            }
        };
    }

    private static List<DocumentChunk> CreateSampleChunks()
    {
        var mockEmbedding = new MockEmbeddingGenerator();
        
        return new List<DocumentChunk>
        {
            new()
            {
                DocumentName = "ml_overview.txt",
                ChunkIndex = 0,
                Text = "Machine learning is a powerful subset of artificial intelligence",
                Embedding = mockEmbedding.GenerateEmbeddingAsync("machine learning ai").Result,
                Hash = "hash1"
            },
            new()
            {
                DocumentName = "ml_overview.txt", 
                ChunkIndex = 1,
                Text = "Supervised learning algorithms learn from labeled training data",
                Embedding = mockEmbedding.GenerateEmbeddingAsync("supervised learning labeled data").Result,
                Hash = "hash2"
            }
        };
    }
}

// Re-use the MockEmbeddingGenerator from the end-to-end tests
public class MockEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly Random _random = new();
    
    public int EmbeddingDimension => 384;

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var hash = text.GetHashCode();
        var seededRandom = new Random(Math.Abs(hash));
        
        var embedding = new float[EmbeddingDimension];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(seededRandom.NextDouble() * 2.0 - 1.0);
        }
        
        // Normalize
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(embedding[i] / magnitude);
        }
        
        return Task.FromResult(embedding);
    }
} 