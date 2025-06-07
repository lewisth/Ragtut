using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Services;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Models;
using Shouldly;
using Microsoft.Data.Sqlite;

namespace Ragtut.Tests.EndToEnd;

public class RagPipelineTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _testDatabasePath;
    private readonly string _testDocumentPath;

    public RagPipelineTests()
    {
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"e2e_test_db_{Guid.NewGuid()}.db");
        _testDocumentPath = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid()}.txt");
        
        // Create test document
        CreateTestDocument();
        
        // Setup DI container
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessing:SupportedExtensions:0"] = ".txt",
                ["DocumentProcessing:SupportedExtensions:1"] = ".pdf",
                ["DocumentProcessing:SupportedExtensions:2"] = ".docx",
                ["DocumentProcessing:ChunkSize"] = "500",
                ["DocumentProcessing:ChunkOverlap"] = "50",
                ["VectorStore:DatabasePath"] = _testDatabasePath
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // Mock embedding generator for testing
        services.AddSingleton<IEmbeddingGenerator, MockEmbeddingGenerator>();
        services.AddSingleton<IDocumentProcessor, DocumentProcessor>();
        services.AddSingleton<IVectorStore>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<SqliteVectorStore>>();
            return new SqliteVectorStore(_testDatabasePath, logger);
        });
    }

    private void CreateTestDocument()
    {
        var content = @"
# Introduction to Machine Learning

Machine learning is a subset of artificial intelligence that focuses on algorithms and statistical models that computer systems use to perform tasks without explicit instructions.

## Types of Machine Learning

### Supervised Learning
Supervised learning uses labeled training data to learn a mapping function from input variables to output variables. Common algorithms include linear regression, decision trees, and neural networks.

### Unsupervised Learning
Unsupervised learning finds hidden patterns in data without labeled examples. Clustering and dimensionality reduction are common unsupervised learning tasks.

### Reinforcement Learning
Reinforcement learning involves training agents to make decisions in an environment to maximize cumulative reward. It's used in robotics, game playing, and autonomous systems.

## Applications

Machine learning has numerous applications including:
- Image recognition and computer vision
- Natural language processing
- Recommendation systems
- Fraud detection
- Medical diagnosis
- Autonomous vehicles

## Conclusion

Machine learning continues to evolve rapidly, with new algorithms and applications being developed constantly. Understanding its fundamentals is crucial for modern technology development.
        ";
        
        File.WriteAllText(_testDocumentPath, content.Trim());
    }

    [Fact]
    public async Task CompleteRagPipeline_ShouldProcessDocumentAndEnableRetrieval()
    {
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        await vectorStore.InitializeAsync();

        var chunks = await documentProcessor.ProcessDocumentAsync(_testDocumentPath);
        await vectorStore.StoreChunksAsync(chunks);

        chunks.ShouldNotBeEmpty();
        chunks.All(c => c.DocumentName == Path.GetFileName(_testDocumentPath)).ShouldBeTrue();
        chunks.All(c => !string.IsNullOrEmpty(c.Text)).ShouldBeTrue();
        chunks.All(c => c.Embedding.Length > 0).ShouldBeTrue();

        var documentExists = await vectorStore.DocumentExistsAsync(Path.GetFileName(_testDocumentPath));
        documentExists.ShouldBeTrue();
    }

    [Fact]
    public async Task RagPipeline_ShouldRetrieveRelevantChunks_ForSpecificQuery()
    {
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        var embeddingGenerator = _serviceProvider.GetRequiredService<IEmbeddingGenerator>();
        
        await vectorStore.InitializeAsync();
        var chunks = await documentProcessor.ProcessDocumentAsync(_testDocumentPath);
        await vectorStore.StoreChunksAsync(chunks);

        var query = "What is supervised learning in machine learning?";
        var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(query);
        var relevantChunks = await vectorStore.SearchSimilarAsync(queryEmbedding, 3, 0.05f);

        
        relevantChunks.ShouldNotBeEmpty();
        relevantChunks.Count().ShouldBeLessThanOrEqualTo(3);
        
        // At least one chunk should contain content about supervised learning
        var hasRelevantContent = relevantChunks.Any(chunk => 
            chunk.Text.ToLower().Contains("supervised") || 
            chunk.Text.ToLower().Contains("labeled"));
        hasRelevantContent.ShouldBeTrue();
    }

    [Fact]
    public async Task RagPipeline_ShouldHandleMultipleDocuments()
    {
        // Arrange
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        
        var secondDocPath = Path.Combine(Path.GetTempPath(), $"test_doc2_{Guid.NewGuid()}.txt");
        File.WriteAllText(secondDocPath, @"
# Deep Learning Fundamentals

Deep learning is a subset of machine learning that uses neural networks with multiple layers.

## Neural Networks
Neural networks are inspired by biological neural networks and consist of interconnected nodes (neurons).

## Applications
Deep learning is used in image recognition, speech processing, and natural language understanding.
        ");

        try
        {
            await vectorStore.InitializeAsync();

            var chunks1 = await documentProcessor.ProcessDocumentAsync(_testDocumentPath);
            var chunks2 = await documentProcessor.ProcessDocumentAsync(secondDocPath);
            
            await vectorStore.StoreChunksAsync(chunks1);
            await vectorStore.StoreChunksAsync(chunks2);

            
            var doc1Exists = await vectorStore.DocumentExistsAsync(Path.GetFileName(_testDocumentPath));
            var doc2Exists = await vectorStore.DocumentExistsAsync(Path.GetFileName(secondDocPath));
            
            doc1Exists.ShouldBeTrue();
            doc2Exists.ShouldBeTrue();

            // Should be able to retrieve from both documents
            var embeddingGenerator = _serviceProvider.GetRequiredService<IEmbeddingGenerator>();
            var query = "neural networks";
            var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(query);
            var results = await vectorStore.SearchSimilarAsync(queryEmbedding, 10, 0.05f);
            
            results.ShouldNotBeEmpty();
            results.Select(r => r.DocumentName).Distinct().Count().ShouldBeGreaterThan(1);
        }
        finally
        {
            if (File.Exists(secondDocPath))
                File.Delete(secondDocPath);
        }
    }

    [Fact]
    public async Task RagPipeline_ShouldHandleDocumentUpdates()
    {
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        await vectorStore.InitializeAsync();

        // Process initial document
        var initialChunks = await documentProcessor.ProcessDocumentAsync(_testDocumentPath);
        await vectorStore.StoreChunksAsync(initialChunks);

        // Update document content with substantial additional content
        var additionalContent = @"

## Quantum Machine Learning

Quantum machine learning represents the intersection of quantum computing and classical machine learning. This emerging field leverages quantum mechanical phenomena such as superposition and entanglement to potentially provide computational advantages for certain machine learning tasks.

### Quantum Advantages

Quantum computers can process information in fundamentally different ways than classical computers. Key advantages include:
- Exponential state space exploration through superposition
- Quantum parallelism for complex optimization problems
- Entanglement for capturing complex correlations in data
- Potential speedups for specific algorithms like quantum support vector machines

### Quantum Algorithms for ML

Several quantum algorithms have been developed specifically for machine learning:
- Quantum Principal Component Analysis (qPCA)
- Quantum k-means clustering
- Quantum neural networks
- Variational quantum eigensolvers for optimization

### Current Challenges

Despite the theoretical advantages, quantum machine learning faces several practical challenges:
- Noise and decoherence in current quantum hardware
- Limited quantum memory and gate fidelity
- The need for quantum-classical hybrid approaches
- Scalability issues with current quantum processors

### Future Prospects

As quantum hardware continues to improve, we expect to see more practical applications of quantum machine learning in areas such as drug discovery, financial modeling, and optimization problems that are intractable for classical computers.
        ";
        
        var updatedContent = File.ReadAllText(_testDocumentPath) + additionalContent;
        File.WriteAllText(_testDocumentPath, updatedContent);

        // Delete old document and process new version
        await vectorStore.DeleteDocumentAsync(Path.GetFileName(_testDocumentPath));
        var updatedChunks = await documentProcessor.ProcessDocumentAsync(_testDocumentPath);
        await vectorStore.StoreChunksAsync(updatedChunks);

        // The updated document should have more chunks due to substantial additional content
        updatedChunks.Count().ShouldBeGreaterThan(initialChunks.Count());
        
        var embeddingGenerator = _serviceProvider.GetRequiredService<IEmbeddingGenerator>();
        var query = "quantum machine learning";
        var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(query);
        var results = await vectorStore.SearchSimilarAsync(queryEmbedding, 5, 0.05f);
        
        results.ShouldNotBeEmpty();
        results.Any(r => r.Text.ToLower().Contains("quantum")).ShouldBeTrue();
    }

    [Fact]
    public async Task RagPipeline_ShouldHandleErrorsGracefully()
    {
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => documentProcessor.ProcessDocumentAsync("non_existent_file.txt"));

        var unsupportedFile = Path.GetTempFileName() + ".xyz";
        try
        {
            File.WriteAllText(unsupportedFile, "test content");
            await Assert.ThrowsAsync<NotSupportedException>(
                () => documentProcessor.ProcessDocumentAsync(unsupportedFile));
        }
        finally
        {
            if (File.Exists(unsupportedFile))
                File.Delete(unsupportedFile);
        }
    }

    [Fact]
    public async Task RagPipeline_ShouldMaintainConsistencyUnderLoad()
    {
        var documentProcessor = _serviceProvider.GetRequiredService<IDocumentProcessor>();
        var vectorStore = _serviceProvider.GetRequiredService<IVectorStore>();
        await vectorStore.InitializeAsync();

        // Create multiple test documents
        var testDocs = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var docPath = Path.Combine(Path.GetTempPath(), $"load_test_doc_{i}_{Guid.NewGuid()}.txt");
            File.WriteAllText(docPath, $"This is test document number {i} with some unique content about topic {i}.");
            testDocs.Add(docPath);
        }

        try
        {
            // Process multiple documents concurrently
            var tasks = testDocs.Select(async docPath =>
            {
                var chunks = await documentProcessor.ProcessDocumentAsync(docPath);
                await vectorStore.StoreChunksAsync(chunks);
                return chunks.Count();
            });

            var results = await Task.WhenAll(tasks);

            
            results.All(count => count > 0).ShouldBeTrue();
            
            // Verify all documents were stored
            foreach (var docPath in testDocs)
            {
                var exists = await vectorStore.DocumentExistsAsync(Path.GetFileName(docPath));
                exists.ShouldBeTrue();
            }
        }
        finally
        {
            // Cleanup
            testDocs.ForEach(docPath => { if (File.Exists(docPath)) File.Delete(docPath); });
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        
        // Clear all SQLite connection pools to ensure connections are closed
        SqliteConnection.ClearAllPools();
        
        // Force garbage collection to ensure all database connections are closed
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // Wait a short time for Windows to release the file handles
        Thread.Sleep(200);
        
        // Try to delete the files, with retries
        var filesToDelete = new[] { _testDocumentPath, _testDatabasePath };
        
        foreach (var filePath in filesToDelete)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
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
}

// Mock embedding generator for testing
public class MockEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly Dictionary<string, float[]> _cachedEmbeddings = new();
    
    public int EmbeddingDimension => 384; // Common embedding dimension

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_cachedEmbeddings.TryGetValue(text, out var cached))
        {
            return Task.FromResult(cached);
        }

        // Generate embeddings based on text content for better semantic similarity
        var embedding = GenerateSemanticEmbedding(text);
        _cachedEmbeddings[text] = embedding;
        
        return Task.FromResult(embedding);
    }

    private float[] GenerateSemanticEmbedding(string text)
    {
        var words = text.ToLower().Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries);
        
        var embedding = new float[EmbeddingDimension];
        
        // Create embedding based on word content for semantic similarity
        var semanticKeywords = new Dictionary<string, int>
        {
            ["supervised"] = 0, ["learning"] = 1, ["machine"] = 1, ["algorithm"] = 2,
            ["neural"] = 3, ["network"] = 3, ["deep"] = 4, ["quantum"] = 5,
            ["computer"] = 6, ["data"] = 7, ["model"] = 8, ["training"] = 9,
            ["classification"] = 10, ["regression"] = 11, ["clustering"] = 12,
            ["reinforcement"] = 13, ["artificial"] = 14, ["intelligence"] = 15,
            ["recognition"] = 16, ["processing"] = 17, ["vision"] = 18,
            ["language"] = 19, ["natural"] = 20, ["recommendation"] = 21,
            ["fraud"] = 22, ["detection"] = 23, ["medical"] = 24, ["diagnosis"] = 25,
            ["autonomous"] = 26, ["vehicle"] = 27, ["robot"] = 28, ["game"] = 29,
            ["decision"] = 30, ["tree"] = 31, ["linear"] = 32, ["unlabeled"] = 33,
            ["labeled"] = 34, ["pattern"] = 35, ["hidden"] = 36, ["dimension"] = 37,
            ["reduction"] = 38, ["agent"] = 39, ["environment"] = 40, ["reward"] = 41,
            ["image"] = 42, ["speech"] = 43, ["text"] = 44, ["system"] = 45
        };
        
        // Initialize with small random values
        var random = new Random(text.GetHashCode());
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 0.1 - 0.05); // Small random baseline
        }
        
        // Add semantic signals based on keywords
        foreach (var word in words)
        {
            if (semanticKeywords.TryGetValue(word, out var semanticIndex))
            {
                // Boost multiple dimensions for this semantic concept
                for (int i = 0; i < 10; i++)
                {
                    var index = (semanticIndex * 10 + i) % embedding.Length;
                    embedding[index] += 0.5f; // Strong signal for semantic similarity
                }
            }
        }
        
        // Normalize
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = (float)(embedding[i] / magnitude);
            }
        }
        
        return embedding;
    }
}

// Extension for Random to allow seeded initialization in .NET
public static class RandomExtensions
{
    public static void Initialize(this Random random, int seed)
    {
        // This is a simple way to create deterministic randomness for testing
        var newRandom = new Random(seed);
        for (int i = 0; i < seed % 100; i++)
        {
            newRandom.Next();
        }
    }
} 