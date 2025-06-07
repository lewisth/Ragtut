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
    public void Constructor_WithInvalidModelPath_ShouldThrowException()
    {
        // Arrange
        var invalidModelPath = "non_existent_model.onnx";

        // Act & Assert
        Assert.Throws<Microsoft.ML.OnnxRuntime.OnnxRuntimeException>(() => 
            new EmbeddingGenerator(invalidModelPath, _mockLogger.Object));
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithValidText_ShouldReturnNormalizedEmbedding()
    {
        // Note: This test requires a valid ONNX model file
        // In a real test environment, you would use a mock or test model
        
        // Skip if no test model available
        var testModelPath = GetTestModelPath();
        if (testModelPath == null)
        {
            return; // Skip test if no model available
        }

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);
        var testText = "This is a test sentence for embedding generation.";

        // Act
        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(testText);

        // Assert
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBeGreaterThan(0);
        
        // Check if embedding is normalized (magnitude should be approximately 1)
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        magnitude.ShouldBe(1.0f, 0.001f);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithEmptyText_ShouldReturnValidEmbedding()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);

        // Act
        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync("");

        // Assert
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithSameText_ShouldReturnSameEmbedding()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);
        var testText = "Consistent test text";

        // Act
        var embedding1 = await _embeddingGenerator.GenerateEmbeddingAsync(testText);
        var embedding2 = await _embeddingGenerator.GenerateEmbeddingAsync(testText);

        // Assert
        embedding1.ShouldBe(embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithDifferentText_ShouldReturnDifferentEmbeddings()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);
        var text1 = "This is the first text";
        var text2 = "This is completely different content";

        // Act
        var embedding1 = await _embeddingGenerator.GenerateEmbeddingAsync(text1);
        var embedding2 = await _embeddingGenerator.GenerateEmbeddingAsync(text2);

        // Assert
        embedding1.ShouldNotBe(embedding2);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _embeddingGenerator.GenerateEmbeddingAsync("test", cts.Token));
    }

    [Fact]
    public void EmbeddingDimension_ShouldReturnConsistentValue()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);

        // Act
        var dimension = _embeddingGenerator.EmbeddingDimension;

        // Assert
        dimension.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithLongText_ShouldHandleGracefully()
    {
        var testModelPath = GetTestModelPath();
        if (testModelPath == null) return;

        // Arrange
        _embeddingGenerator = new EmbeddingGenerator(testModelPath, _mockLogger.Object);
        var longText = string.Join(" ", Enumerable.Repeat("word", 10000));

        // Act
        var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(longText);

        // Assert
        embedding.ShouldNotBeNull();
        embedding.Length.ShouldBeGreaterThan(0);
    }

    private string? GetTestModelPath()
    {
        // In a real test environment, you would have a test ONNX model
        // For now, return null to skip tests that require a model
        var testModelPath = Path.Combine("TestData", "test_model.onnx");
        return File.Exists(testModelPath) ? testModelPath : null;
    }

    public void Dispose()
    {
        _embeddingGenerator?.Dispose();
    }
}

// Helper class to create a mock ONNX model for testing
public static class TestModelHelper
{
    public static void CreateMockModel(string path)
    {
        // This would create a minimal ONNX model for testing purposes
        // Implementation depends on your specific testing needs
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        
        // Create a dummy file for now - in real tests you'd create a proper ONNX model
        File.WriteAllText(path, "mock_model_data");
    }
} 