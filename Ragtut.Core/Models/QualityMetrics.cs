namespace Ragtut.Core.Models;

public class RelevanceMetrics
{
    public string Query { get; set; } = string.Empty;
    public int RetrievedChunkCount { get; set; }
    public float AverageSimilarity { get; set; }
    public float MinSimilarity { get; set; }
    public float MaxSimilarity { get; set; }
    public float AverageSemanticScore { get; set; }
    public Dictionary<string, int> SemanticScoreDistribution { get; set; } = new();
    public float? Precision { get; set; }
    public float? Recall { get; set; }
    public float? F1Score { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ChunkQualityMetrics
{
    public int TotalChunks { get; set; }
    public double AverageTextLength { get; set; }
    public int MinTextLength { get; set; }
    public int MaxTextLength { get; set; }
    public double TextLengthStandardDeviation { get; set; }
    public int EmbeddingDimension { get; set; }
    public int UniqueDocuments { get; set; }
    public double AverageChunksPerDocument { get; set; }
    public int DuplicateHashCount { get; set; }
    public EmbeddingMagnitudeStats? EmbeddingMagnitudeStats { get; set; }
    public double EmbeddingCoherenceScore { get; set; }
    public DateTime Timestamp { get; set; }
}

public class EmbeddingMagnitudeStats
{
    public double Average { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double StandardDeviation { get; set; }
}

public class RetrievalPerformanceMetrics
{
    public int TotalTestCases { get; set; }
    public TimeSpan AverageRetrievalTime { get; set; }
    public TimeSpan TotalEvaluationTime { get; set; }
    public float AveragePrecision { get; set; }
    public float AverageRecall { get; set; }
    public float AverageF1Score { get; set; }
    public float AverageSimilarity { get; set; }
    public List<RetrievalResult> Results { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class RetrievalResult
{
    public QueryTestCase TestCase { get; set; } = null!;
    public List<DocumentChunk> RetrievedChunks { get; set; } = new();
    public RelevanceMetrics RelevanceMetrics { get; set; } = null!;
    public TimeSpan RetrievalTime { get; set; }
}

public class QueryTestCase
{
    public string Query { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 10;
    public float SimilarityThreshold { get; set; } = 0.1f;
    public List<DocumentChunk>? ExpectedChunks { get; set; }
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
}

public class QualityReport
{
    public ChunkQualityMetrics ChunkQuality { get; set; } = null!;
    public RetrievalPerformanceMetrics RetrievalPerformance { get; set; } = null!;
    public float OverallScore { get; set; }
    public List<string> Recommendations { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class OptimizationMetrics
{
    public ChunkSizeOptimizationResult? ChunkSizeOptimization { get; set; }
    public CachePerformanceMetrics? CachePerformance { get; set; }
    public BatchProcessingMetrics? BatchProcessing { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ChunkSizeOptimizationResult
{
    public int OriginalChunkSize { get; set; }
    public int OptimalChunkSize { get; set; }
    public double PerformanceImprovement { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public List<ChunkSizeTestResult> TestResults { get; set; } = new();
}

public class ChunkSizeTestResult
{
    public int ChunkSize { get; set; }
    public float AverageRelevanceScore { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public int TotalChunks { get; set; }
}

public class CachePerformanceMetrics
{
    public int TotalRequests { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0;
    public TimeSpan AverageCacheRetrievalTime { get; set; }
    public TimeSpan AverageDirectRetrievalTime { get; set; }
    public double PerformanceGain => AverageDirectRetrievalTime.TotalMilliseconds > 0 ? 
        AverageDirectRetrievalTime.TotalMilliseconds / AverageCacheRetrievalTime.TotalMilliseconds : 1;
    public long CacheSizeBytes { get; set; }
    public DateTime Timestamp { get; set; }
}

public class BatchProcessingMetrics
{
    public int TotalDocuments { get; set; }
    public int BatchSize { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public TimeSpan AverageDocumentProcessingTime { get; set; }
    public double DocumentsPerSecond => TotalProcessingTime.TotalSeconds > 0 ? 
        TotalDocuments / TotalProcessingTime.TotalSeconds : 0;
    public int TotalChunksGenerated { get; set; }
    public int ErrorCount { get; set; }
    public double SuccessRate => TotalDocuments > 0 ? 
        (double)(TotalDocuments - ErrorCount) / TotalDocuments : 0;
    public DateTime Timestamp { get; set; }
} 