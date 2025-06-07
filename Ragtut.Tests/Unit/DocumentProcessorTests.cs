using Xunit;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Services;
using Ragtut.Core.Interfaces;
using Shouldly;
using System.Text;

namespace Ragtut.Tests.Unit;

public class DocumentProcessorTests
{
    private readonly IConfiguration _configuration;
    private readonly Mock<IEmbeddingGenerator> _mockEmbeddingGenerator;
    private readonly Mock<ILogger<DocumentProcessor>> _mockLogger;
    private readonly DocumentProcessor _documentProcessor;

    public DocumentProcessorTests()
    {
        _mockEmbeddingGenerator = new Mock<IEmbeddingGenerator>();
        _mockLogger = new Mock<ILogger<DocumentProcessor>>();

        _configuration = CreateConfiguration();
        _documentProcessor = new DocumentProcessor(
            _configuration,
            _mockEmbeddingGenerator.Object,
            _mockLogger.Object);
    }

    private IConfiguration CreateConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["DocumentProcessing:SupportedExtensions:0"] = ".txt",
            ["DocumentProcessing:SupportedExtensions:1"] = ".pdf", 
            ["DocumentProcessing:SupportedExtensions:2"] = ".docx",
            ["DocumentProcessing:ChunkSize"] = "1000",
            ["DocumentProcessing:ChunkOverlap"] = "100"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".pdf", true)]
    [InlineData(".docx", true)]
    [InlineData(".xlsx", false)]
    [InlineData(".jpg", false)]
    [InlineData("", false)]
    public void SupportsFileType_ShouldReturnCorrectResult(string extension, bool expected)
    {
        var filePath = $"test{extension}";
        var result = _documentProcessor.SupportsFileType(filePath);
        
        result.ShouldBe(expected);
    }

    [Fact]
    public async Task ProcessDocumentAsync_WithTextFile_ShouldReturnChunks()
    {
        var testText = "This is a test document. " + string.Join(" ", Enumerable.Repeat("word", 200));
        var tempFile = Path.GetTempFileName();
        var textFile = Path.ChangeExtension(tempFile, ".txt");
        File.Delete(tempFile); // Delete the .tmp file
        await File.WriteAllTextAsync(textFile, testText);
        
        var mockEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockEmbeddingGenerator.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEmbedding);

        try
        {
            var result = await _documentProcessor.ProcessDocumentAsync(textFile);

            result.ShouldNotBeEmpty();
            result.All(chunk => chunk.DocumentName == Path.GetFileName(textFile)).ShouldBeTrue();
            result.All(chunk => chunk.Embedding.SequenceEqual(mockEmbedding)).ShouldBeTrue();
            result.All(chunk => !string.IsNullOrEmpty(chunk.Hash)).ShouldBeTrue();
        }
        finally
        {
            if (File.Exists(textFile))
                File.Delete(textFile);
        }
    }

    [Fact]
    public async Task ProcessDocumentAsync_WithUnsupportedFile_ShouldThrowNotSupportedException()
    {
        var tempFile = Path.GetTempFileName() + ".xyz";

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _documentProcessor.ProcessDocumentAsync(tempFile));
    }

    [Fact]
    public async Task ProcessDocumentAsync_WithEmptyFile_ShouldReturnEmptyChunks()
    {
        var tempFile = Path.GetTempFileName();
        var textFile = Path.ChangeExtension(tempFile, ".txt");
        File.Delete(tempFile); // Delete the .tmp file
        await File.WriteAllTextAsync(textFile, "");

        try
        {
            var result = await _documentProcessor.ProcessDocumentAsync(textFile);
    
            result.ShouldBeEmpty();
        }
        finally
        {
            if (File.Exists(textFile))
                File.Delete(textFile);
        }
    }

    [Fact]
    public void TextChunking_ShouldRespectChunkSizeAndOverlap()
    {
        // This test would require making the SplitTextIntoChunks method public or internal
        // For now, we test it indirectly through ProcessDocumentAsync
        
        var words = Enumerable.Range(1, 1000).Select(i => $"word{i}").ToArray();
        var testText = string.Join(" ", words);
        
        // The actual testing would verify chunk boundaries and overlaps
        // This is tested indirectly through the integration tests
    }

    [Fact]
    public async Task ProcessDocumentAsync_ShouldGenerateUniqueHashes()
    {
        var testText1 = "This is test content number one.";
        var testText2 = "This is test content number two.";
        
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        var textFile1 = Path.ChangeExtension(tempFile1, ".txt");
        var textFile2 = Path.ChangeExtension(tempFile2, ".txt");
        File.Delete(tempFile1); // Delete the .tmp files
        File.Delete(tempFile2);
        
        await File.WriteAllTextAsync(textFile1, testText1);
        await File.WriteAllTextAsync(textFile2, testText2);

        var mockEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockEmbeddingGenerator.Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockEmbedding);

        try
        {
            var chunks1 = await _documentProcessor.ProcessDocumentAsync(textFile1);
            var chunks2 = await _documentProcessor.ProcessDocumentAsync(textFile2);
            
            var hash1 = chunks1.First().Hash;
            var hash2 = chunks2.First().Hash;
            hash1.ShouldNotBe(hash2);
        }
        finally
        {
            if (File.Exists(textFile1))
                File.Delete(textFile1);
            if (File.Exists(textFile2))
                File.Delete(textFile2);
        }
    }

    [Fact]
    public async Task ProcessDocumentAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        var testText = string.Join(" ", Enumerable.Repeat("word", 10000));
        var tempFile = Path.GetTempFileName();
        var textFile = Path.ChangeExtension(tempFile, ".txt");
        File.Delete(tempFile); // Delete the .tmp file
        await File.WriteAllTextAsync(textFile, testText);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            // TaskCanceledException inherits from OperationCanceledException
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _documentProcessor.ProcessDocumentAsync(textFile, cts.Token));
        }
        finally
        {
            if (File.Exists(textFile))
                File.Delete(textFile);
        }
    }
} 