using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Models;
using Ragtut.Core.Services;
using Spectre.Console;
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
    .AddSingleton<IEmbeddingGenerator>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<EmbeddingGenerator>>();
        var modelPath = config["EmbeddingModel:ModelPath"] ?? throw new InvalidOperationException("EmbeddingModel:ModelPath is required");
        return new EmbeddingGenerator(modelPath, logger);
    })
    .AddSingleton<IVectorStore>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<SqliteVectorStore>>();
        var dbPath = config["VectorStore:DatabasePath"] ?? throw new InvalidOperationException("VectorStore:DatabasePath is required");
        return new SqliteVectorStore(dbPath, logger);
    })
    .AddSingleton<OllamaApiClient>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var baseUrl = config["Ollama:BaseUrl"] ?? throw new InvalidOperationException("Ollama:BaseUrl is required");
        return new OllamaApiClient(baseUrl);
    })
    .BuildServiceProvider();

var logger = services.GetRequiredService<ILogger<Program>>();
var config = services.GetRequiredService<IConfiguration>();
var vectorStore = services.GetRequiredService<IVectorStore>();
var embeddingGenerator = services.GetRequiredService<IEmbeddingGenerator>();
var ollama = services.GetRequiredService<OllamaApiClient>();

// Initialize vector store
await vectorStore.InitializeAsync();

// Initialize Ollama
var ollamaModel = config["Ollama:Model"] ?? throw new InvalidOperationException("Ollama:Model is required");
var temperature = config.GetValue<float>("Ollama:Temperature");
var maxTokens = config.GetValue<int>("Ollama:MaxTokens");
var maxChunks = config.GetValue<int>("RAG:MaxChunks");
var similarityThreshold = config.GetValue<float>("RAG:SimilarityThreshold");

// Set the selected model
ollama.SelectedModel = ollamaModel;

// Conversation history
var conversationHistory = new List<(string Role, string Content)>();

// Main chat loop
AnsiConsole.MarkupLine("[green]Welcome to RagChat! Type 'exit' to quit.[/]");

while (true)
{
    var question = AnsiConsole.Ask<string>("[blue]You:[/] ");
    
    if (question.ToLower() == "exit")
        break;

    try
    {
        // Generate embedding for the question
        var questionEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(question);

        // Search for relevant chunks
        var relevantChunks = await SearchRelevantChunksAsync(vectorStore, questionEmbedding, maxChunks, similarityThreshold);

        if (!relevantChunks.Any())
        {
            AnsiConsole.MarkupLine("[yellow]I couldn't find any relevant information in the documents to answer your question.[/]");
            continue;
        }

        // Build the prompt with context
        var prompt = BuildPrompt(question, relevantChunks, conversationHistory);

        // Generate response using Ollama
        var response = await GenerateResponseAsync(ollama, prompt, temperature);

        // Add to conversation history
        conversationHistory.Add(("user", question));
        conversationHistory.Add(("assistant", response));

        // Display response
        AnsiConsole.MarkupLine($"[green]Assistant:[/] {response}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing question");
        AnsiConsole.MarkupLine("[red]An error occurred while processing your question. Please try again.[/]");
    }
}

async Task<IEnumerable<DocumentChunk>> SearchRelevantChunksAsync(
    IVectorStore vectorStore,
    float[] questionEmbedding,
    int maxChunks,
    float similarityThreshold)
{
    return await vectorStore.SearchSimilarAsync(
        questionEmbedding,
        maxChunks,
        similarityThreshold);
}

string BuildPrompt(
    string question,
    IEnumerable<DocumentChunk> relevantChunks,
    List<(string Role, string Content)> conversationHistory)
{
    var prompt = new StringBuilder();

    // System instructions
    prompt.AppendLine("You are a helpful AI assistant that answers questions based on the provided context. Follow these rules:");
    prompt.AppendLine("1. Only use information from the provided context to answer questions");
    prompt.AppendLine("2. Always include citations in the format [Document: filename.pdf, Page: X]");
    prompt.AppendLine("3. If the context doesn't contain enough information, say so");
    prompt.AppendLine("4. Maintain a conversational tone");
    prompt.AppendLine();

    // Add context
    prompt.AppendLine("Context:");
    foreach (var chunk in relevantChunks)
    {
        prompt.AppendLine($"Document: {chunk.DocumentName}, Page: {chunk.PageNumber}");
        prompt.AppendLine(chunk.Text);
        prompt.AppendLine();
    }

    // Add conversation history
    if (conversationHistory.Any())
    {
        prompt.AppendLine("Previous conversation:");
        foreach (var (role, content) in conversationHistory.TakeLast(4))
        {
            prompt.AppendLine($"{role}: {content}");
        }
        prompt.AppendLine();
    }

    // Add current question
    prompt.AppendLine($"Question: {question}");
    prompt.AppendLine("Answer:");

    return prompt.ToString();
}

async Task<string> GenerateResponseAsync(
    OllamaApiClient ollama,
    string prompt,
    float temperature)
{
    var responseBuilder = new StringBuilder();
    await foreach (var stream in ollama.GenerateAsync(prompt))
    {
        if (stream?.Response != null)
        {
            responseBuilder.Append(stream.Response);
        }
    }
    return responseBuilder.ToString();
}
