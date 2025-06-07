using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Models;
using System.Security.Cryptography;
using System.Text;

namespace Ragtut.Core.Services;

public class DocumentProcessor : IDocumentProcessor
{
    private readonly IConfiguration _configuration;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        IConfiguration configuration,
        IEmbeddingGenerator embeddingGenerator,
        ILogger<DocumentProcessor> logger)
    {
        _configuration = configuration;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public bool SupportsFileType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        var supportedExtensions = _configuration.GetSection("DocumentProcessing:SupportedExtensions").Get<string[]>();
        return supportedExtensions?.Contains(extension) ?? false;
    }

    public async Task<IEnumerable<DocumentChunk>> ProcessDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        var text = extension switch
        {
            ".pdf" => await ExtractTextFromPdfAsync(filePath),
            ".txt" => await File.ReadAllTextAsync(filePath, cancellationToken),
            ".docx" => await ExtractTextFromDocxAsync(filePath),
            _ => throw new NotSupportedException($"File type {extension} is not supported")
        };

        var chunks = SplitTextIntoChunks(text);
        var documentChunks = new List<DocumentChunk>();
        var chunkSize = _configuration.GetValue<int>("DocumentProcessing:ChunkSize");
        var chunkOverlap = _configuration.GetValue<int>("DocumentProcessing:ChunkOverlap");

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedding = await _embeddingGenerator.GenerateEmbeddingAsync(chunk, cancellationToken);
            var hash = ComputeHash(chunk);

            documentChunks.Add(new DocumentChunk
            {
                DocumentName = Path.GetFileName(filePath),
                PageNumber = i / (chunkSize - chunkOverlap) + 1,
                ChunkIndex = i,
                Text = chunk,
                Embedding = embedding,
                IndexedAt = DateTime.UtcNow,
                Hash = hash
            });
        }

        return documentChunks;
    }

    private Task<string> ExtractTextFromPdfAsync(string filePath)
    {
        var text = new StringBuilder();
        using var pdfReader = new PdfReader(filePath);
        using var pdfDocument = new PdfDocument(pdfReader);

        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
        {
            var page = pdfDocument.GetPage(i);
            var strategy = new LocationTextExtractionStrategy();
            var currentText = PdfTextExtractor.GetTextFromPage(page, strategy);
            text.AppendLine(currentText);
        }

        return Task.FromResult(text.ToString());
    }

    private Task<string> ExtractTextFromDocxAsync(string filePath)
    {
        var text = new StringBuilder();
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document.Body;

        if (body != null)
        {
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                text.AppendLine(paragraph.InnerText);
            }
        }

        return Task.FromResult(text.ToString());
    }

    private List<string> SplitTextIntoChunks(string text)
    {
        var chunkSize = _configuration.GetValue<int>("DocumentProcessing:ChunkSize");
        var chunkOverlap = _configuration.GetValue<int>("DocumentProcessing:ChunkOverlap");
        var chunks = new List<string>();
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        var currentSize = 0;

        foreach (var word in words)
        {
            if (currentSize + word.Length + 1 > chunkSize)
            {
                chunks.Add(string.Join(" ", currentChunk));
                currentChunk = currentChunk.Skip(chunkOverlap).ToList();
                currentSize = currentChunk.Sum(w => w.Length + 1);
            }

            currentChunk.Add(word);
            currentSize += word.Length + 1;
        }

        if (currentChunk.Any())
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }

    private string ComputeHash(string text)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(hashBytes);
    }
} 