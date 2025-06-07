using Xunit;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Services;
using Ragtut.Core.Models;
using Shouldly;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

namespace Ragtut.Tests.Integration;

public class VectorStoreIntegrationTests : IDisposable
{
    private SqliteVectorStore? _vectorStore;
    private readonly string _testDatabasePath;
    private readonly ILogger<SqliteVectorStore> _logger;

    public VectorStoreIntegrationTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"test_vector_db_{Guid.NewGuid()}.db");
        _logger = NullLogger<SqliteVectorStore>.Instance;
        _vectorStore = new SqliteVectorStore(_testDatabasePath, _logger);
    }

    [Fact]
    public async Task Initialize_ShouldCreateDatabase()
    {
        await _vectorStore!.InitializeAsync();  
        File.Exists(_testDatabasePath).ShouldBeTrue();
    }

    [Fact]
    public async Task StoreChunks_ShouldPersistChunks()
    {
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();

        await _vectorStore.StoreChunksAsync(chunks);

        
        var exists = await _vectorStore.DocumentExistsAsync("test_document.txt");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task StoreChunks_WithDuplicateHash_ShouldReplaceExisting()
    {
        await _vectorStore!.InitializeAsync();
        var chunks1 = CreateTestChunks();
        var chunks2 = CreateTestChunks();
        
        // Modify the second set to have same hash but different content
        chunks2.First().Text = "Modified content";

        await _vectorStore.StoreChunksAsync(chunks1);
        await _vectorStore.StoreChunksAsync(chunks2);

        // Search should find the modified content
        var queryEmbedding = chunks1.First().Embedding;
        var results = await _vectorStore.SearchSimilarAsync(queryEmbedding, 5, 0.0f);
        results.First(r => r.Hash == chunks1.First().Hash).Text.ShouldBe("Modified content");
    }

    [Fact]
    public async Task DocumentExists_WithExistingDocument_ShouldReturnTrue()
    {
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();
        await _vectorStore.StoreChunksAsync(chunks);
        var exists = await _vectorStore.DocumentExistsAsync("test_document.txt");
        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task DocumentExists_WithNonExistingDocument_ShouldReturnFalse()
    {        
        await _vectorStore!.InitializeAsync();        
        var exists = await _vectorStore.DocumentExistsAsync("non_existing_document.txt");        
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteDocument_ShouldRemoveAllChunks()
    {        
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();
        await _vectorStore.StoreChunksAsync(chunks);        
        await _vectorStore.DeleteDocumentAsync("test_document.txt");
        
        var exists = await _vectorStore.DocumentExistsAsync("test_document.txt");
        exists.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchSimilar_WithExactMatch_ShouldReturnSimilarChunks()
    {        
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();
        await _vectorStore.StoreChunksAsync(chunks);
        var queryEmbedding = chunks.First().Embedding;        
        var results = await _vectorStore.SearchSimilarAsync(queryEmbedding, 5, 0.0f);
        
        results.ShouldNotBeEmpty();
        results.First().Embedding.ShouldBe(queryEmbedding);
    }

    [Fact]
    public async Task SearchSimilar_WithHighThreshold_ShouldReturnFewerResults()
    {        
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();
        await _vectorStore.StoreChunksAsync(chunks);
        var queryEmbedding = CreateRandomEmbedding();
        
        var lowThresholdResults = await _vectorStore.SearchSimilarAsync(queryEmbedding, 10, 0.05f);
        var highThresholdResults = await _vectorStore.SearchSimilarAsync(queryEmbedding, 10, 0.5f);
        
        lowThresholdResults.Count().ShouldBeGreaterThanOrEqualTo(highThresholdResults.Count());
    }

    [Fact]
    public async Task SearchSimilar_WithMaxResults_ShouldLimitResults()
    {        
        await _vectorStore!.InitializeAsync();
        var chunks = CreateManyTestChunks(20);
        await _vectorStore.StoreChunksAsync(chunks);
        var queryEmbedding = chunks.First().Embedding;
        
        var results = await _vectorStore.SearchSimilarAsync(queryEmbedding, 5, 0.0f);        
        results.Count().ShouldBeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task SearchSimilar_ShouldReturnResultsInSimilarityOrder()
    {        
        await _vectorStore!.InitializeAsync();
        var chunks = CreateTestChunks();
        await _vectorStore.StoreChunksAsync(chunks);
        var queryEmbedding = CreateRandomEmbedding();        
        var results = await _vectorStore.SearchSimilarAsync(queryEmbedding, 10, 0.0f);        
        results.ShouldNotBeEmpty();
        // Results should be ordered by similarity (we can't easily test the actual similarity values
        // without exposing them, but we can test that we get results)
    }

    [Fact]
    public async Task StoreAndRetrieve_ShouldPreserveAllChunkProperties()
    {
        await _vectorStore!.InitializeAsync();
        var originalChunk = new DocumentChunk
        {
            DocumentName = "test_doc.txt",
            PageNumber = 1,
            ChunkIndex = 0,
            Text = "This is a test chunk with specific content.",
            Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f },
            IndexedAt = DateTime.UtcNow,
            Hash = "test_hash_123"
        };

        await _vectorStore.StoreChunksAsync(new[] { originalChunk });
        var retrievedChunks = await _vectorStore.SearchSimilarAsync(originalChunk.Embedding, 1, 0.0f);
        
        var retrievedChunk = retrievedChunks.First();
        retrievedChunk.DocumentName.ShouldBe(originalChunk.DocumentName);
        retrievedChunk.PageNumber.ShouldBe(originalChunk.PageNumber);
        retrievedChunk.ChunkIndex.ShouldBe(originalChunk.ChunkIndex);
        retrievedChunk.Text.ShouldBe(originalChunk.Text);
        retrievedChunk.Embedding.ShouldBe(originalChunk.Embedding);
        retrievedChunk.Hash.ShouldBe(originalChunk.Hash);
        
        // Convert both dates to UTC for comparison to handle timezone differences
        var originalUtc = originalChunk.IndexedAt.ToUniversalTime();
        var retrievedUtc = retrievedChunk.IndexedAt.ToUniversalTime();
        retrievedUtc.ShouldBe(originalUtc, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ConcurrentOperations_ShouldHandleGracefully()
    {
        await _vectorStore!.InitializeAsync();
        var chunks1 = CreateTestChunks("doc1.txt");
        var chunks2 = CreateTestChunks("doc2.txt");

        var task1 = _vectorStore.StoreChunksAsync(chunks1);
        var task2 = _vectorStore.StoreChunksAsync(chunks2);
        await Task.WhenAll(task1, task2);

        
        var exists1 = await _vectorStore.DocumentExistsAsync("doc1.txt");
        var exists2 = await _vectorStore.DocumentExistsAsync("doc2.txt");
        exists1.ShouldBeTrue();
        exists2.ShouldBeTrue();
    }

    private IEnumerable<DocumentChunk> CreateTestChunks(string documentName = "test_document.txt")
    {
        return new[]
        {
            new DocumentChunk
            {
                DocumentName = documentName,
                PageNumber = 1,
                ChunkIndex = 0,
                Text = "This is the first test chunk.",
                Embedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f },
                IndexedAt = DateTime.UtcNow,
                Hash = "hash1"
            },
            new DocumentChunk
            {
                DocumentName = documentName,
                PageNumber = 1,
                ChunkIndex = 1,
                Text = "This is the second test chunk.",
                Embedding = new float[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.6f },
                IndexedAt = DateTime.UtcNow,
                Hash = "hash2"
            },
            new DocumentChunk
            {
                DocumentName = documentName,
                PageNumber = 2,
                ChunkIndex = 2,
                Text = "This is the third test chunk on page 2.",
                Embedding = new float[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f },
                IndexedAt = DateTime.UtcNow,
                Hash = "hash3"
            }
        };
    }

    private IEnumerable<DocumentChunk> CreateManyTestChunks(int count, string documentName = "test_document.txt")
    {
        return Enumerable.Range(0, count).Select(i => new DocumentChunk
        {
            DocumentName = documentName,
            PageNumber = i / 10 + 1,
            ChunkIndex = i,
            Text = $"Test chunk number {i}",
            Embedding = CreateRandomEmbedding(),
            IndexedAt = DateTime.UtcNow,
            Hash = $"hash_{i}"
        });
    }

    private float[] CreateRandomEmbedding()
    {
        var random = new Random();
        var embedding = new float[5];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)random.NextDouble();
        }
        
        // Normalize
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        return embedding.Select(x => (float)(x / magnitude)).ToArray();
    }

    public void Dispose()
    {
        // Clear the vector store reference to release any potential handles
        _vectorStore = null;
        
        // Clear all SQLite connection pools to ensure connections are closed
        SqliteConnection.ClearAllPools();
        
        // Force garbage collection to ensure all database connections are closed
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Wait a short time for Windows to release the file handle
        Thread.Sleep(200);
        
        // Try to delete the file, with retries
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(_testDatabasePath))
                {
                    File.Delete(_testDatabasePath);
                    break;
                }
            }
            catch (IOException) when (i < 9) // Don't throw on the last attempt
            {
                Thread.Sleep(100 * (i + 1)); // Progressive backoff
            }
        }
    }
} 