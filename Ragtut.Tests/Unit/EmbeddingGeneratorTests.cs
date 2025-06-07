using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Services;
using Shouldly;

namespace Ragtut.Tests.Unit;

public class EmbeddingGeneratorTests : IDisposable
{
    private readonly Mock<ILogger<EmbeddingGenerator>> _mockLogger;
    private EmbeddingGenerator? _embeddingGenerator;

    public EmbeddingGeneratorTests()
    {
        _mockLogger = new Mock<ILogger<EmbeddingGenerator>>();
    }

    [Fact]
    public void Constructor_WithValidModelPath_ShouldSucceed()
    {

        var mockModelPath = "models/test_model.onnx"; // Path doesn't matter for TF-IDF approach

        var generator = new EmbeddingGenerator(mockModelPath, _mockLogger.Object);
        generator.ShouldNotBeNull();
        generator.Dispose();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ShouldReturnNormalizedEmbedding()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var testText = "This is a test sentence for embedding generation.";


        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(testText);


        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBe(384); // Fixed dimension for TF-IDF approach
        
        // Check if embedding is normalized (magnitude should be approximately 1)
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.ShouldBe(1.0f, 0.001f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithEmptyText_ShouldReturnValidEmbedding()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);

        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync("");
       
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBe(384);
        
        // Empty text should still produce a valid embedding (all zeros when normalized)
        var allZeros = embedding.All(x => Math.Abs(x) < 0.001f);
        allZeros.ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithSameText_ShouldReturnSameEmbedding()
    {
        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var testText = "Consistent test text";

        var embedding1 = await _embeddingGenerator.GenerateEmbeddingAsync(testText);
        var embedding2 = await _embeddingGenerator.GenerateEmbeddingAsync(testText);
     
        embedding1.ShouldBe(embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithDifferentText_ShouldReturnDifferentEmbeddings()
    {
        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var text1 = "This is the first text about machine learning";
        var text2 = "This is completely different content about cooking";
        
        var embedding1 = await _embeddingGenerator.GenerateEmbeddingAsync(text1);
        var embedding2 = await _embeddingGenerator.GenerateEmbeddingAsync(text2);
       
        embedding1.ShouldNotBe(embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithSimilarText_ShouldReturnSimilarEmbeddings()
    {        
        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var text1 = "machine learning algorithms";
        var text2 = "algorithms for machine learning";

        var embedding1 = await _embeddingGenerator.GenerateEmbeddingAsync(text1);
        var embedding2 = await _embeddingGenerator.GenerateEmbeddingAsync(text2);

         // Calculate cosine similarity
        var similarity = CalculateCosineSimilarity(embedding1, embedding2);
        similarity.ShouldBeGreaterThan(0.5f); // Similar texts should have reasonable similarity
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithCancellation_ShouldRespectCancellationToken()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _embeddingGenerator.GenerateEmbeddingAsync("test", cts.Token));
    }

    [Fact]
    public void EmbeddingDimension_ShouldReturn384()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);


        var dimension = _embeddingGenerator.EmbeddingDimension;

        
        dimension.ShouldBe(384); // Fixed dimension for TF-IDF approach
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithLongText_ShouldHandleGracefully()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var longText = string.Join(" ", Enumerable.Repeat("word", 10000));


        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(longText);

        
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBe(384);
        
        // Should still be normalized
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.ShouldBe(1.0f, 0.001f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithSpecialCharacters_ShouldHandleGracefully()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var textWithSpecialChars = "Hello! How are you? This has punctuation: colons, semicolons; and [brackets].";


        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(textWithSpecialChars);

        
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBe(384);
        
        // Should be normalized
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.ShouldBe(1.0f, 0.001f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithLayeredArchitectureTerms_ShouldCreateMeaningfulEmbedding()
    {

        _embeddingGenerator = new EmbeddingGenerator("dummy_path", _mockLogger.Object);
        var architectureText = "layered architecture pattern with presentation layer business logic layer data access layer";
        var unrelatedText = "cooking recipes with ingredients and preparation steps";


        var archEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(architectureText);
        var cookingEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(unrelatedText);

        
        archEmbedding.ShouldNotBeNull();
        cookingEmbedding.ShouldNotBeNull();
        
        // These should be quite different
        var similarity = CalculateCosineSimilarity(archEmbedding, cookingEmbedding);
        similarity.ShouldBeLessThan(0.5f); // Unrelated texts should have low similarity
    }

    private float CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length)
            return 0f;

        var dotProduct = 0f;
        var magnitude1 = 0f;
        var magnitude2 = 0f;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = (float)Math.Sqrt(magnitude1);
        magnitude2 = (float)Math.Sqrt(magnitude2);

        if (magnitude1 == 0f || magnitude2 == 0f)
            return 0f;

        return dotProduct / (magnitude1 * magnitude2);
    }

    public void Dispose()
    {
        _embeddingGenerator?.Dispose();
    }
} 