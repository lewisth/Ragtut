using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ragtut.Core.Services;
using Ragtut.Core.Models;
using Ragtut.Core.Interfaces;

namespace Ragtut.Tests.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
[MinColumn, MaxColumn, MeanColumn, MedianColumn]
public class RagPerformanceBenchmarks
{
    private DocumentProcessor _documentProcessor = null!;
    private SqliteVectorStore _vectorStore = null!;
    private MockEmbeddingGenerator _embeddingGenerator = null!;
    private string _testDocumentPath = null!;
    private string _testDatabasePath = null!;
    private List<DocumentChunk> _testChunks = null!;
    private float[] _queryEmbedding = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup configuration
        var configData = new Dictionary<string, string?>
        {
            ["DocumentProcessing:SupportedExtensions:0"] = ".txt",
            ["DocumentProcessing:SupportedExtensions:1"] = ".pdf",
            ["DocumentProcessing:SupportedExtensions:2"] = ".docx",
            ["DocumentProcessing:ChunkSize"] = "1000",
            ["DocumentProcessing:ChunkOverlap"] = "100"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var logger = NullLogger<DocumentProcessor>.Instance;
        var vectorLogger = NullLogger<SqliteVectorStore>.Instance;

        // Setup components
        _embeddingGenerator = new MockEmbeddingGenerator();
        _documentProcessor = new DocumentProcessor(configuration, _embeddingGenerator, logger);
        
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"benchmark_db_{Guid.NewGuid()}.db");
        _vectorStore = new SqliteVectorStore(_testDatabasePath, vectorLogger);
        
        // Initialize vector store
        _vectorStore.InitializeAsync().Wait();

        // Create test document
        CreateTestDocument();
        
        // Pre-generate test chunks
        _testChunks = GenerateTestChunks(1000).ToList();
        _queryEmbedding = _embeddingGenerator.GenerateEmbeddingAsync("test query").Result;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testDocumentPath))
            File.Delete(_testDocumentPath);
        if (File.Exists(_testDatabasePath))
            File.Delete(_testDatabasePath);
    }

    private void CreateTestDocument()
    {
        _testDocumentPath = Path.Combine(Path.GetTempPath(), $"benchmark_doc_{Guid.NewGuid()}.txt");
        
        var content = string.Join("\n\n", Enumerable.Range(1, 100).Select(i => 
            $"This is paragraph {i} containing various information about topic {i}. " +
            $"It includes detailed explanations, examples, and technical details that would be " +
            $"commonly found in documentation or research papers. The content covers multiple " +
            $"aspects of the subject matter and provides comprehensive coverage of the topic."));
        
        File.WriteAllText(_testDocumentPath, content);
    }

    private IEnumerable<DocumentChunk> GenerateTestChunks(int count)
    {
        return Enumerable.Range(0, count).Select(i => new DocumentChunk
        {
            Id = i,
            DocumentName = $"test_doc_{i % 10}.txt",
            PageNumber = i / 10 + 1,
            ChunkIndex = i,
            Text = $"This is test chunk {i} with content about topic {i % 50}. " +
                   $"It contains relevant information for testing and benchmarking purposes. " +
                   $"The text is of sufficient length to simulate real-world document chunks.",
            Embedding = _embeddingGenerator.GenerateEmbeddingAsync($"chunk {i}").Result,
            IndexedAt = DateTime.UtcNow,
            Hash = $"hash_{i}"
        });
    }

    [Benchmark]
    public async Task<IEnumerable<DocumentChunk>> ProcessDocument_Small()
    {
        var smallDoc = Path.GetTempFileName();
        File.WriteAllText(smallDoc, string.Join(" ", Enumerable.Repeat("word", 100)));
        
        try
        {
            return await _documentProcessor.ProcessDocumentAsync(smallDoc);
        }
        finally
        {
            File.Delete(smallDoc);
        }
    }

    [Benchmark]
    public async Task<IEnumerable<DocumentChunk>> ProcessDocument_Medium()
    {
        var mediumDoc = Path.GetTempFileName();
        File.WriteAllText(mediumDoc, string.Join(" ", Enumerable.Repeat("word", 1000)));
        
        try
        {
            return await _documentProcessor.ProcessDocumentAsync(mediumDoc);
        }
        finally
        {
            File.Delete(mediumDoc);
        }
    }

    [Benchmark]
    public async Task<IEnumerable<DocumentChunk>> ProcessDocument_Large()
    {
        return await _documentProcessor.ProcessDocumentAsync(_testDocumentPath);
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task StoreChunks(int chunkCount)
    {
        var chunks = _testChunks.Take(chunkCount);
        await _vectorStore.StoreChunksAsync(chunks);
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(5)]
    [Arguments(10)]
    [Arguments(50)]
    public async Task<IEnumerable<DocumentChunk>> SearchSimilar(int maxResults)
    {
        return await _vectorStore.SearchSimilarAsync(_queryEmbedding, maxResults, 0.1f);
    }

    [Benchmark]
    public async Task<float[]> GenerateEmbedding_Short()
    {
        return await _embeddingGenerator.GenerateEmbeddingAsync("Short text");
    }

    [Benchmark]
    public async Task<float[]> GenerateEmbedding_Medium()
    {
        var mediumText = string.Join(" ", Enumerable.Repeat("word", 100));
        return await _embeddingGenerator.GenerateEmbeddingAsync(mediumText);
    }

    [Benchmark]
    public async Task<float[]> GenerateEmbedding_Long()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 1000));
        return await _embeddingGenerator.GenerateEmbeddingAsync(longText);
    }

    [Benchmark]
    public async Task CompleteRagPipeline()
    {
        var tempDoc = Path.GetTempFileName();
        var tempDb = Path.Combine(Path.GetTempPath(), $"temp_benchmark_{Guid.NewGuid()}.db");
        
        try
        {
            File.WriteAllText(tempDoc, "Test document content for RAG pipeline benchmark");
            
            var tempVectorStore = new SqliteVectorStore(tempDb, NullLogger<SqliteVectorStore>.Instance);
            await tempVectorStore.InitializeAsync();
            
            var chunks = await _documentProcessor.ProcessDocumentAsync(tempDoc);
            await tempVectorStore.StoreChunksAsync(chunks);
            
            var query = await _embeddingGenerator.GenerateEmbeddingAsync("test query");
            var results = await tempVectorStore.SearchSimilarAsync(query, 5, 0.1f);
        }
        finally
        {
            if (File.Exists(tempDoc)) File.Delete(tempDoc);
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    public async Task BatchEmbeddingGeneration(int batchSize)
    {
        var texts = Enumerable.Range(0, batchSize)
            .Select(i => $"Text number {i} for batch embedding generation")
            .ToList();

        var tasks = texts.Select(text => _embeddingGenerator.GenerateEmbeddingAsync(text));
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task ConcurrentVectorSearch()
    {
        var queries = new List<float[]>();
        for (int i = 0; i < 10; i++)
        {
            var query = await _embeddingGenerator.GenerateEmbeddingAsync("concurrent query");
            queries.Add(query);
        }

        var tasks = queries.Select(query => 
            _vectorStore.SearchSimilarAsync(query, 5, 0.1f));
        
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task DocumentExistsCheck()
    {
        await _vectorStore.DocumentExistsAsync("test_document.txt");
    }

    [Benchmark]
    public async Task DeleteDocument()
    {
        // Create a temporary document to delete
        var tempChunks = GenerateTestChunks(10);
        await _vectorStore.StoreChunksAsync(tempChunks);
        await _vectorStore.DeleteDocumentAsync(tempChunks.First().DocumentName);
    }
}

// Dedicated benchmark for memory usage patterns
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RagMemoryBenchmarks
{
    private MockEmbeddingGenerator _embeddingGenerator = null!;
    private SqliteVectorStore _vectorStore = null!;
    private string _testDatabasePath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _embeddingGenerator = new MockEmbeddingGenerator();
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"memory_benchmark_db_{Guid.NewGuid()}.db");
        _vectorStore = new SqliteVectorStore(_testDatabasePath, NullLogger<SqliteVectorStore>.Instance);
        _vectorStore.InitializeAsync().Wait();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testDatabasePath))
            File.Delete(_testDatabasePath);
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    [Arguments(100000)]
    public async Task LargeDatasetStorage(int chunkCount)
    {
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < chunkCount; i++)
        {
            var embedding = await _embeddingGenerator.GenerateEmbeddingAsync($"chunk {i}");
            chunks.Add(new DocumentChunk
            {
                DocumentName = $"doc_{i % 100}.txt",
                PageNumber = i / 100 + 1,
                ChunkIndex = i,
                Text = $"Large dataset chunk {i} with substantial content for memory testing",
                Embedding = embedding,
                IndexedAt = DateTime.UtcNow,
                Hash = $"hash_{i}"
            });
        }

        await _vectorStore.StoreChunksAsync(chunks);
    }

    [Benchmark]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task LargeEmbeddingBatch(int batchSize)
    {
        var tasks = Enumerable.Range(0, batchSize)
            .Select(i => _embeddingGenerator.GenerateEmbeddingAsync($"Memory test text {i}"));
        
        await Task.WhenAll(tasks);
    }
}

// Mock embedding generator for benchmarking
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

// To run benchmarks, use:
// dotnet run --project Ragtut.Tests --configuration Release
// Or create a separate console app that references this test project 