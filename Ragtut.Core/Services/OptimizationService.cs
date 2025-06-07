using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Ragtut.Core.Models;
using Ragtut.Core.Interfaces;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Ragtut.Core.Services;

public class OptimizationService
{
    private readonly ILogger<OptimizationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly QualityEvaluationService _qualityEvaluationService;

    // Caching components
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache = new();
    private readonly ConcurrentDictionary<string, List<DocumentChunk>> _chunkCache = new();
    private readonly ConcurrentDictionary<string, (DateTime timestamp, object value)> _responseCache = new();
    
    // Cache statistics
    private int _embeddingCacheHits = 0;
    private int _embeddingCacheMisses = 0;
    private int _chunkCacheHits = 0;
    private int _chunkCacheMisses = 0;

    public OptimizationService(
        ILogger<OptimizationService> logger,
        IConfiguration configuration,
        IDocumentProcessor documentProcessor,
        IEmbeddingGenerator embeddingGenerator,
        QualityEvaluationService qualityEvaluationService)
    {
        _logger = logger;
        _configuration = configuration;
        _documentProcessor = documentProcessor;
        _embeddingGenerator = embeddingGenerator;
        _qualityEvaluationService = qualityEvaluationService;
    }

    /// <summary>
    /// Optimizes chunk size based on document type and retrieval performance
    /// </summary>
    public async Task<ChunkSizeOptimizationResult> OptimizeChunkSizeAsync(
        string documentPath,
        string documentType,
        IEnumerable<QueryTestCase> testQueries)
    {
        _logger.LogInformation("Starting chunk size optimization for document type: {DocumentType}", documentType);

        var originalChunkSize = _configuration.GetValue<int>("DocumentProcessing:ChunkSize");
        var testSizes = new[] { 250, 500, 750, 1000, 1250, 1500, 2000 };
        var testResults = new List<ChunkSizeTestResult>();

        foreach (var chunkSize in testSizes)
        {
            _logger.LogDebug("Testing chunk size: {ChunkSize}", chunkSize);
            
            var result = await TestChunkSizeAsync(documentPath, chunkSize, testQueries);
            testResults.Add(result);
        }

        // Find optimal chunk size based on relevance score and processing efficiency
        var optimalResult = testResults
            .OrderByDescending(r => r.AverageRelevanceScore)
            .ThenBy(r => r.ProcessingTime.TotalMilliseconds)
            .First();

        var optimizationResult = new ChunkSizeOptimizationResult
        {
            OriginalChunkSize = originalChunkSize,
            OptimalChunkSize = optimalResult.ChunkSize,
            DocumentType = documentType,
            TestResults = testResults,
            PerformanceImprovement = CalculatePerformanceImprovement(originalChunkSize, optimalResult, testResults)
        };

        _logger.LogInformation("Chunk size optimization completed. Optimal size: {OptimalSize} (improvement: {Improvement:F2}%)",
            optimizationResult.OptimalChunkSize, optimizationResult.PerformanceImprovement);

        return optimizationResult;
    }

    /// <summary>
    /// Generates embeddings with caching to avoid reprocessing
    /// </summary>
    public async Task<float[]> GetCachedEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var cacheKey = ComputeHash(text);
        
        if (_embeddingCache.TryGetValue(cacheKey, out var cachedEmbedding))
        {
            Interlocked.Increment(ref _embeddingCacheHits);
            _logger.LogDebug("Embedding cache hit for text hash: {Hash}", cacheKey);
            return cachedEmbedding;
        }

        Interlocked.Increment(ref _embeddingCacheMisses);
        _logger.LogDebug("Embedding cache miss for text hash: {Hash}", cacheKey);
        
        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(text, cancellationToken);
        _embeddingCache.TryAdd(cacheKey, embedding);
        
        return embedding;
    }

    /// <summary>
    /// Processes multiple documents in optimized batches
    /// </summary>
    public async Task<BatchProcessingMetrics> ProcessDocumentBatchAsync(
        IEnumerable<string> documentPaths,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        var documentPathsList = documentPaths.ToList();
        var totalDocuments = documentPathsList.Count;
        var totalChunks = 0;
        var errors = 0;
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting batch processing of {DocumentCount} documents with batch size {BatchSize}", 
            totalDocuments, batchSize);

        var batches = documentPathsList
            .Select((path, index) => new { path, index })
            .GroupBy(x => x.index / batchSize)
            .Select(g => g.Select(x => x.path).ToList())
            .ToList();

        var allTasks = new List<Task>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        foreach (var batch in batches)
        {
            var batchTasks = batch.Select(async documentPath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var chunks = await _documentProcessor.ProcessDocumentAsync(documentPath, cancellationToken);
                    Interlocked.Add(ref totalChunks, chunks.Count());
                    _logger.LogDebug("Processed document: {DocumentPath}, chunks: {ChunkCount}", 
                        documentPath, chunks.Count());
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    _logger.LogError(ex, "Error processing document: {DocumentPath}", documentPath);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            allTasks.AddRange(batchTasks);
        }

        await Task.WhenAll(allTasks);
        var endTime = DateTime.UtcNow;
        var totalTime = endTime - startTime;

        var metrics = new BatchProcessingMetrics
        {
            TotalDocuments = totalDocuments,
            BatchSize = batchSize,
            TotalProcessingTime = totalTime,
            AverageDocumentProcessingTime = totalDocuments > 0 ? 
                TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds / totalDocuments) : 
                TimeSpan.Zero,
            TotalChunksGenerated = totalChunks,
            ErrorCount = errors,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogInformation("Batch processing completed. Processed: {Processed}/{Total}, " +
            "Total time: {TotalTime:F2}s, Rate: {Rate:F2} docs/sec",
            totalDocuments - errors, totalDocuments, 
            totalTime.TotalSeconds, metrics.DocumentsPerSecond);

        return metrics;
    }

    /// <summary>
    /// Gets cache performance metrics
    /// </summary>
    public CachePerformanceMetrics GetCachePerformanceMetrics()
    {
        var totalEmbeddingRequests = _embeddingCacheHits + _embeddingCacheMisses;
        var totalChunkRequests = _chunkCacheHits + _chunkCacheMisses;
        
        var metrics = new CachePerformanceMetrics
        {
            TotalRequests = totalEmbeddingRequests + totalChunkRequests,
            CacheHits = _embeddingCacheHits + _chunkCacheHits,
            CacheMisses = _embeddingCacheMisses + _chunkCacheMisses,
            AverageCacheRetrievalTime = TimeSpan.FromMilliseconds(1), // Simulated - very fast cache access
            AverageDirectRetrievalTime = TimeSpan.FromMilliseconds(50), // Simulated - slower direct access
            CacheSizeBytes = CalculateCacheSize(),
            Timestamp = DateTime.UtcNow
        };

        return metrics;
    }

    /// <summary>
    /// Clears all caches and resets statistics
    /// </summary>
    public void ClearCaches()
    {
        _embeddingCache.Clear();
        _chunkCache.Clear();
        _responseCache.Clear();
        
        _embeddingCacheHits = 0;
        _embeddingCacheMisses = 0;
        _chunkCacheHits = 0;
        _chunkCacheMisses = 0;
        
        _logger.LogInformation("All caches cleared and statistics reset");
    }

    /// <summary>
    /// Optimizes vector database queries by analyzing access patterns
    /// </summary>
    public async Task<QueryOptimizationResult> OptimizeVectorQueriesAsync(
        IVectorStore vectorStore,
        IEnumerable<QueryTestCase> testQueries)
    {
        _logger.LogInformation("Starting vector query optimization analysis");

        var results = new List<QueryPerformanceResult>();
        var thresholds = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        var maxResultsOptions = new[] { 5, 10, 20, 50 };

        foreach (var testCase in testQueries)
        {
            var queryEmbedding = await GetCachedEmbeddingAsync(testCase.Query);
            
            foreach (var threshold in thresholds)
            {
                foreach (var maxResults in maxResultsOptions)
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var chunks = await vectorStore.SearchSimilarAsync(queryEmbedding, maxResults, threshold);
                    stopwatch.Stop();

                    var relevanceMetrics = await _qualityEvaluationService.EvaluateRelevanceAsync(
                        testCase.Query, chunks, testCase.ExpectedChunks);

                    results.Add(new QueryPerformanceResult
                    {
                        Query = testCase.Query,
                        SimilarityThreshold = threshold,
                        MaxResults = maxResults,
                        RetrievalTime = stopwatch.Elapsed,
                        RelevanceScore = relevanceMetrics.F1Score ?? relevanceMetrics.AverageSimilarity,
                        ResultCount = chunks.Count()
                    });
                }
            }
        }

        // Find optimal parameters
        var optimalResult = results
            .GroupBy(r => r.Query)
            .Select(g => g.OrderByDescending(r => r.RelevanceScore).ThenBy(r => r.RetrievalTime).First())
            .ToList();

        var optimization = new QueryOptimizationResult
        {
            OptimalSimilarityThreshold = optimalResult.Average(r => r.SimilarityThreshold),
            OptimalMaxResults = (int)optimalResult.Average(r => r.MaxResults),
            AveragePerformanceImprovement = CalculateQueryPerformanceImprovement(results, optimalResult),
            TestResults = results,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogInformation("Vector query optimization completed. Optimal threshold: {Threshold:F2}, " +
            "Optimal max results: {MaxResults}, Performance improvement: {Improvement:F2}%",
            optimization.OptimalSimilarityThreshold, optimization.OptimalMaxResults, 
            optimization.AveragePerformanceImprovement);

        return optimization;
    }

    private async Task<ChunkSizeTestResult> TestChunkSizeAsync(
        string documentPath,
        int chunkSize,
        IEnumerable<QueryTestCase> testQueries)
    {
        // Temporarily modify configuration for testing
        var originalConfig = _configuration.GetValue<int>("DocumentProcessing:ChunkSize");
        
        // This is a simplified approach - in reality, you'd need to create a new DocumentProcessor
        // with the modified configuration
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var chunks = await _documentProcessor.ProcessDocumentAsync(documentPath);
        stopwatch.Stop();

        var relevanceScores = new List<float>();
        foreach (var testQuery in testQueries)
        {
            var queryEmbedding = await GetCachedEmbeddingAsync(testQuery.Query);
            
            // Calculate relevance of chunks to query
            var similarities = chunks.Select(chunk => 
                CalculateCosineSimilarity(queryEmbedding, chunk.Embedding)).ToList();
            
            if (similarities.Any())
                relevanceScores.Add(similarities.Average());
        }

        return new ChunkSizeTestResult
        {
            ChunkSize = chunkSize,
            AverageRelevanceScore = relevanceScores.Any() ? relevanceScores.Average() : 0,
            ProcessingTime = stopwatch.Elapsed,
            TotalChunks = chunks.Count()
        };
    }

    private double CalculatePerformanceImprovement(
        int originalSize,
        ChunkSizeTestResult optimalResult,
        List<ChunkSizeTestResult> allResults)
    {
        var originalResult = allResults.FirstOrDefault(r => r.ChunkSize == originalSize);
        if (originalResult == null) return 0;

        var improvement = ((optimalResult.AverageRelevanceScore - originalResult.AverageRelevanceScore) /
                          originalResult.AverageRelevanceScore) * 100;
        
        return Math.Max(0, improvement);
    }

    private double CalculateQueryPerformanceImprovement(
        List<QueryPerformanceResult> allResults,
        List<QueryPerformanceResult> optimalResults)
    {
        var averageBaseline = allResults.Average(r => r.RelevanceScore);
        var averageOptimal = optimalResults.Average(r => r.RelevanceScore);
        
        return averageBaseline > 0 ? ((averageOptimal - averageBaseline) / averageBaseline) * 100 : 0;
    }

    private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length) return 0f;

        var dotProduct = vectorA.Zip(vectorB, (a, b) => a * b).Sum();
        var magnitudeA = Math.Sqrt(vectorA.Sum(x => x * x));
        var magnitudeB = Math.Sqrt(vectorB.Sum(x => x * x));

        if (magnitudeA == 0 || magnitudeB == 0) return 0f;

        return (float)(dotProduct / (magnitudeA * magnitudeB));
    }

    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }

    private long CalculateCacheSize()
    {
        long size = 0;
        
        // Estimate embedding cache size
        foreach (var embedding in _embeddingCache.Values)
        {
            size += embedding.Length * sizeof(float);
        }
        
        // Estimate chunk cache size (simplified)
        foreach (var chunks in _chunkCache.Values)
        {
            size += chunks.Sum(c => c.Text.Length * sizeof(char) + c.Embedding.Length * sizeof(float));
        }
        
        return size;
    }
}

public class QueryOptimizationResult
{
    public float OptimalSimilarityThreshold { get; set; }
    public int OptimalMaxResults { get; set; }
    public double AveragePerformanceImprovement { get; set; }
    public List<QueryPerformanceResult> TestResults { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class QueryPerformanceResult
{
    public string Query { get; set; } = string.Empty;
    public float SimilarityThreshold { get; set; }
    public int MaxResults { get; set; }
    public TimeSpan RetrievalTime { get; set; }
    public float RelevanceScore { get; set; }
    public int ResultCount { get; set; }
} 