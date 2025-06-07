using Microsoft.Extensions.Logging;
using Ragtut.Core.Models;
using Ragtut.Core.Interfaces;
using System.Text.Json;

namespace Ragtut.Core.Services;

public class QualityEvaluationService
{
    private readonly ILogger<QualityEvaluationService> _logger;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public QualityEvaluationService(
        ILogger<QualityEvaluationService> logger,
        IEmbeddingGenerator embeddingGenerator)
    {
        _logger = logger;
        _embeddingGenerator = embeddingGenerator;
    }

    /// <summary>
    /// Evaluates the relevance of retrieved chunks to a query
    /// </summary>
    public async Task<RelevanceMetrics> EvaluateRelevanceAsync(
        string query,
        IEnumerable<DocumentChunk> retrievedChunks,
        IEnumerable<DocumentChunk>? groundTruthChunks = null)
    {
        var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(query);
        var similarities = new List<float>();
        var semanticScores = new List<float>();

        foreach (var chunk in retrievedChunks)
        {
            // Calculate cosine similarity
            var similarity = CalculateCosineSimilarity(queryEmbedding, chunk.Embedding);
            similarities.Add(similarity);

            // Calculate semantic relevance score
            var semanticScore = await CalculateSemanticRelevanceAsync(query, chunk.Text);
            semanticScores.Add(semanticScore);
        }

        var metrics = new RelevanceMetrics
        {
            Query = query,
            RetrievedChunkCount = retrievedChunks.Count(),
            AverageSimilarity = similarities.Any() ? similarities.Average() : 0,
            MinSimilarity = similarities.Any() ? similarities.Min() : 0,
            MaxSimilarity = similarities.Any() ? similarities.Max() : 0,
            AverageSemanticScore = semanticScores.Any() ? semanticScores.Average() : 0,
            SemanticScoreDistribution = CalculateDistribution(semanticScores),
            Timestamp = DateTime.UtcNow
        };

        if (groundTruthChunks != null)
        {
            var precisionRecall = CalculatePrecisionRecall(retrievedChunks, groundTruthChunks);
            metrics.Precision = precisionRecall.Precision;
            metrics.Recall = precisionRecall.Recall;
            metrics.F1Score = precisionRecall.F1Score;
        }

        _logger.LogInformation("Relevance evaluation completed for query: {Query}. Average similarity: {AvgSim:F3}", 
            query, metrics.AverageSimilarity);

        return metrics;
    }

    /// <summary>
    /// Evaluates chunk quality metrics
    /// </summary>
    public ChunkQualityMetrics EvaluateChunkQuality(IEnumerable<DocumentChunk> chunks)
    {
        var chunkList = chunks.ToList();
        var textLengths = chunkList.Select(c => c.Text.Length).ToList();
        var embeddings = chunkList.Select(c => c.Embedding).ToList();

        var metrics = new ChunkQualityMetrics
        {
            TotalChunks = chunkList.Count,
            AverageTextLength = textLengths.Any() ? textLengths.Average() : 0,
            MinTextLength = textLengths.Any() ? textLengths.Min() : 0,
            MaxTextLength = textLengths.Any() ? textLengths.Max() : 0,
            TextLengthStandardDeviation = CalculateStandardDeviation(textLengths),
            EmbeddingDimension = embeddings.FirstOrDefault()?.Length ?? 0,
            UniqueDocuments = chunkList.Select(c => c.DocumentName).Distinct().Count(),
            AverageChunksPerDocument = chunkList.Count > 0 ? 
                (double)chunkList.Count / chunkList.Select(c => c.DocumentName).Distinct().Count() : 0,
            DuplicateHashCount = chunkList.GroupBy(c => c.Hash).Count(g => g.Count() > 1),
            Timestamp = DateTime.UtcNow
        };

        // Calculate embedding quality metrics
        if (embeddings.Any() && embeddings.First().Length > 0)
        {
            metrics.EmbeddingMagnitudeStats = CalculateEmbeddingMagnitudeStats(embeddings);
            metrics.EmbeddingCoherenceScore = CalculateEmbeddingCoherence(embeddings);
        }

        _logger.LogInformation("Chunk quality evaluation completed. Total chunks: {Count}, Avg length: {AvgLen:F1}", 
            metrics.TotalChunks, metrics.AverageTextLength);

        return metrics;
    }

    /// <summary>
    /// Evaluates retrieval performance metrics
    /// </summary>
    public async Task<RetrievalPerformanceMetrics> EvaluateRetrievalPerformanceAsync(
        IVectorStore vectorStore,
        IEnumerable<QueryTestCase> testCases)
    {
        var results = new List<RetrievalResult>();
        var totalTime = TimeSpan.Zero;

        foreach (var testCase in testCases)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(testCase.Query);
            var retrievedChunks = await vectorStore.SearchSimilarAsync(
                queryEmbedding, testCase.MaxResults, testCase.SimilarityThreshold);
            
            stopwatch.Stop();
            totalTime += stopwatch.Elapsed;

            var relevanceMetrics = await EvaluateRelevanceAsync(
                testCase.Query, retrievedChunks, testCase.ExpectedChunks);

            results.Add(new RetrievalResult
            {
                TestCase = testCase,
                RetrievedChunks = retrievedChunks.ToList(),
                RelevanceMetrics = relevanceMetrics,
                RetrievalTime = stopwatch.Elapsed
            });
        }

        var metrics = new RetrievalPerformanceMetrics
        {
            TotalTestCases = testCases.Count(),
            AverageRetrievalTime = results.Any() ? 
                TimeSpan.FromMilliseconds(results.Average(r => r.RetrievalTime.TotalMilliseconds)) : 
                TimeSpan.Zero,
            TotalEvaluationTime = totalTime,
            AveragePrecision = results.Any() ? results.Average(r => r.RelevanceMetrics.Precision ?? 0) : 0,
            AverageRecall = results.Any() ? results.Average(r => r.RelevanceMetrics.Recall ?? 0) : 0,
            AverageF1Score = results.Any() ? results.Average(r => r.RelevanceMetrics.F1Score ?? 0) : 0,
            AverageSimilarity = results.Any() ? results.Average(r => r.RelevanceMetrics.AverageSimilarity) : 0,
            Results = results,
            Timestamp = DateTime.UtcNow
        };

        _logger.LogInformation("Retrieval performance evaluation completed. {TestCount} test cases, " +
            "avg time: {AvgTime:F2}ms, avg F1: {AvgF1:F3}", 
            metrics.TotalTestCases, metrics.AverageRetrievalTime.TotalMilliseconds, metrics.AverageF1Score);

        return metrics;
    }

    /// <summary>
    /// Generates a comprehensive quality report
    /// </summary>
    public async Task<QualityReport> GenerateQualityReportAsync(
        IVectorStore vectorStore,
        IEnumerable<QueryTestCase> testCases,
        IEnumerable<DocumentChunk> allChunks)
    {
        _logger.LogInformation("Generating comprehensive quality report...");

        var chunkQuality = EvaluateChunkQuality(allChunks);
        var retrievalPerformance = await EvaluateRetrievalPerformanceAsync(vectorStore, testCases);
        
        var report = new QualityReport
        {
            ChunkQuality = chunkQuality,
            RetrievalPerformance = retrievalPerformance,
            OverallScore = CalculateOverallScore(chunkQuality, retrievalPerformance),
            Recommendations = GenerateRecommendations(chunkQuality, retrievalPerformance),
            GeneratedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Quality report generated. Overall score: {Score:F2}/10", report.OverallScore);

        return report;
    }

    /// <summary>
    /// Exports quality metrics to JSON file
    /// </summary>
    public async Task ExportMetricsAsync(QualityReport report, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(report, options);
        await File.WriteAllTextAsync(filePath, json);
        
        _logger.LogInformation("Quality metrics exported to: {FilePath}", filePath);
    }

    private float CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            return 0f;

        var dotProduct = vectorA.Zip(vectorB, (a, b) => a * b).Sum();
        var magnitudeA = Math.Sqrt(vectorA.Sum(x => x * x));
        var magnitudeB = Math.Sqrt(vectorB.Sum(x => x * x));

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0f;

        return (float)(dotProduct / (magnitudeA * magnitudeB));
    }

    private Task<float> CalculateSemanticRelevanceAsync(string query, string chunkText)
    {
        // This is a simplified semantic relevance calculation
        // In a real implementation, you might use more sophisticated NLP techniques
        
        var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunkWords = chunkText.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var intersection = queryWords.Intersect(chunkWords).Count();
        var union = queryWords.Union(chunkWords).Count();
        
        var result = union > 0 ? (float)intersection / union : 0f;
        return Task.FromResult(result);
    }

    private (float Precision, float Recall, float F1Score) CalculatePrecisionRecall(
        IEnumerable<DocumentChunk> retrieved,
        IEnumerable<DocumentChunk> groundTruth)
    {
        var retrievedHashes = retrieved.Select(c => c.Hash).ToHashSet();
        var groundTruthHashes = groundTruth.Select(c => c.Hash).ToHashSet();
        
        var truePositives = retrievedHashes.Intersect(groundTruthHashes).Count();
        var precision = retrievedHashes.Count > 0 ? (float)truePositives / retrievedHashes.Count : 0f;
        var recall = groundTruthHashes.Count > 0 ? (float)truePositives / groundTruthHashes.Count : 0f;
        var f1Score = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0f;
        
        return (precision, recall, f1Score);
    }

    private Dictionary<string, int> CalculateDistribution(IEnumerable<float> values)
    {
        var distribution = new Dictionary<string, int>();
        var valueList = values.ToList();
        
        if (!valueList.Any()) return distribution;
        
        var ranges = new[] { "0.0-0.2", "0.2-0.4", "0.4-0.6", "0.6-0.8", "0.8-1.0" };
        
        foreach (var range in ranges)
        {
            distribution[range] = 0;
        }
        
        foreach (var value in valueList)
        {
            var rangeIndex = Math.Min((int)(value * 5), 4);
            distribution[ranges[rangeIndex]]++;
        }
        
        return distribution;
    }

    private double CalculateStandardDeviation(IEnumerable<int> values)
    {
        var valueList = values.ToList();
        if (valueList.Count <= 1) return 0;
        
        var mean = valueList.Average();
        var sumSquaredDifferences = valueList.Sum(x => Math.Pow(x - mean, 2));
        return Math.Sqrt(sumSquaredDifferences / (valueList.Count - 1));
    }

    private EmbeddingMagnitudeStats CalculateEmbeddingMagnitudeStats(IEnumerable<float[]> embeddings)
    {
        var magnitudes = embeddings.Select(e => Math.Sqrt(e.Sum(x => x * x))).ToList();
        
        return new EmbeddingMagnitudeStats
        {
            Average = magnitudes.Average(),
            Min = magnitudes.Min(),
            Max = magnitudes.Max(),
            StandardDeviation = CalculateStandardDeviation(magnitudes.Select(m => (int)(m * 1000)))
        };
    }

    private double CalculateEmbeddingCoherence(IEnumerable<float[]> embeddings)
    {
        var embeddingList = embeddings.ToList();
        if (embeddingList.Count < 2) return 1.0;
        
        var similarities = new List<float>();
        for (int i = 0; i < embeddingList.Count; i++)
        {
            for (int j = i + 1; j < embeddingList.Count; j++)
            {
                similarities.Add(CalculateCosineSimilarity(embeddingList[i], embeddingList[j]));
            }
        }
        
        return similarities.Any() ? similarities.Average() : 0;
    }

    private float CalculateOverallScore(ChunkQualityMetrics chunkQuality, RetrievalPerformanceMetrics retrievalPerformance)
    {
        // Weighted scoring formula (out of 10)
        var chunkScore = Math.Min(10f, 
            (float)(chunkQuality.TotalChunks > 0 ? 5f : 0f) +
            (float)(chunkQuality.DuplicateHashCount == 0 ? 2f : Math.Max(0f, 2f - chunkQuality.DuplicateHashCount / 10f)) +
            (float)(chunkQuality.EmbeddingCoherenceScore * 3f));
        
        var retrievalScore = Math.Min(10f,
            (float)(retrievalPerformance.AverageF1Score * 5f) +
            (float)(retrievalPerformance.AverageSimilarity * 3f) +
            (float)(retrievalPerformance.AverageRetrievalTime.TotalMilliseconds < 100 ? 2f : 
                   Math.Max(0f, 2f - retrievalPerformance.AverageRetrievalTime.TotalMilliseconds / 1000f)));
        
        return (chunkScore + retrievalScore) / 2f;
    }

    private List<string> GenerateRecommendations(ChunkQualityMetrics chunkQuality, RetrievalPerformanceMetrics retrievalPerformance)
    {
        var recommendations = new List<string>();
        
        if (chunkQuality.DuplicateHashCount > 0)
        {
            recommendations.Add($"Found {chunkQuality.DuplicateHashCount} duplicate chunks. Consider implementing deduplication.");
        }
        
        if (chunkQuality.TextLengthStandardDeviation > chunkQuality.AverageTextLength * 0.5)
        {
            recommendations.Add("High variation in chunk sizes. Consider optimizing chunk size configuration.");
        }
        
        if (retrievalPerformance.AverageF1Score < 0.7)
        {
            recommendations.Add("Low F1 score indicates poor retrieval quality. Consider adjusting similarity thresholds or improving embeddings.");
        }
        
        if (retrievalPerformance.AverageRetrievalTime.TotalMilliseconds > 500)
        {
            recommendations.Add("High retrieval latency. Consider optimizing vector search or adding caching.");
        }
        
        if (chunkQuality.EmbeddingCoherenceScore < 0.3)
        {
            recommendations.Add("Low embedding coherence. Consider using a better embedding model or preprocessing text.");
        }
        
        if (recommendations.Count == 0)
        {
            recommendations.Add("System is performing well. Continue monitoring for consistency.");
        }
        
        return recommendations;
    }
} 