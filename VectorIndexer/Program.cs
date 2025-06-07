using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Services;
using Spectre.Console;
using System.Security.Cryptography;
using System.Text;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var services = new ServiceCollection()
    .AddLogging(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    })
    .AddSingleton<IConfiguration>(configuration)
    .AddSingleton<IDocumentProcessor, DocumentProcessor>()
    .AddSingleton<IEmbeddingGenerator>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<EmbeddingGenerator>>();
        var modelPath = config["EmbeddingModel:ModelPath"] ?? throw new InvalidOperationException("EmbeddingModel:ModelPath configuration is required");
        return new EmbeddingGenerator(modelPath, logger);
    })
    .AddSingleton<IVectorStore>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<SqliteVectorStore>>();
        var dbPath = config["VectorStore:DatabasePath"] ?? throw new InvalidOperationException("VectorStore:DatabasePath configuration is required");
        return new SqliteVectorStore(dbPath, logger);
    })
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILogger<Program>>();
var config = services.GetRequiredService<IConfiguration>();

// Create necessary directories
Directory.CreateDirectory("models");
Directory.CreateDirectory("data");

// Download the embedding model if it doesn't exist
var modelPath = config["EmbeddingModel:ModelPath"];
if (!File.Exists(modelPath))
{
    AnsiConsole.MarkupLine("[yellow]Embedding model not found. Please download the a model and place it in the models directory.[/]");
    return;
}

// Initialize vector store
var vectorStore = services.GetRequiredService<IVectorStore>();
await vectorStore.InitializeAsync();

// Process documents
var documentsPath = Path.Combine(Directory.GetCurrentDirectory(), "documents");
if (!Directory.Exists(documentsPath))
{
    AnsiConsole.MarkupLine("[red]Documents directory not found![/]");
    return;
}

var supportedExtensions = config.GetSection("DocumentProcessing:SupportedExtensions").Get<string[]>() ?? new[] { ".txt", ".pdf", ".docx" };
var files = Directory.GetFiles(documentsPath, "*.*", SearchOption.AllDirectories)
    .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
    .ToList();

if (!files.Any())
{
    AnsiConsole.MarkupLine("[yellow]No supported documents found in the documents directory.[/]");
    return;
}

var progress = AnsiConsole.Progress();
await progress.StartAsync(async ctx =>
{
    var task = ctx.AddTask("[green]Processing documents[/]", maxValue: files.Count);
    
    foreach (var file in files)
    {
        try
        {
            var documentProcessor = services.GetRequiredService<IDocumentProcessor>();
            if (!documentProcessor.SupportsFileType(file))
            {
                logger.LogWarning("Unsupported file type: {File}", file);
                task.Increment(1);
                continue;
            }

            var documentName = Path.GetFileName(file);

            // Check if document already exists
            if (await vectorStore.DocumentExistsAsync(documentName))
            {
                logger.LogInformation("Document already indexed: {File}", file);
                task.Increment(1);
                continue;
            }

            // Process document
            var chunks = await documentProcessor.ProcessDocumentAsync(file);
            await vectorStore.StoreChunksAsync(chunks);

            logger.LogInformation("Successfully processed: {File}", file);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing file: {File}", file);
        }

        task.Increment(1);
    }
});

AnsiConsole.MarkupLine("[green]Document indexing completed![/]");
