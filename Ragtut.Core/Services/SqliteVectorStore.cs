using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Interfaces;
using Ragtut.Core.Models;
using System.Text;
using System.Linq;

namespace Ragtut.Core.Services;

public class SqliteVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteVectorStore> _logger;

    public SqliteVectorStore(string databasePath, ILogger<SqliteVectorStore> logger)
    {
        _connectionString = $"Data Source={databasePath}";
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS document_chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_name TEXT NOT NULL,
                page_number INTEGER NOT NULL,
                chunk_index INTEGER NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL,
                indexed_at TEXT NOT NULL,
                hash TEXT NOT NULL,
                UNIQUE(document_name, hash)
            );

            CREATE INDEX IF NOT EXISTS idx_document_name ON document_chunks(document_name);
            CREATE INDEX IF NOT EXISTS idx_hash ON document_chunks(hash);";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task StoreChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var chunk in chunks)
            {
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO document_chunks 
                    (document_name, page_number, chunk_index, text, embedding, indexed_at, hash)
                    VALUES (@document_name, @page_number, @chunk_index, @text, @embedding, @indexed_at, @hash)";

                command.Parameters.AddWithValue("@document_name", chunk.DocumentName);
                command.Parameters.AddWithValue("@page_number", chunk.PageNumber);
                command.Parameters.AddWithValue("@chunk_index", chunk.ChunkIndex);
                command.Parameters.AddWithValue("@text", chunk.Text);
                command.Parameters.AddWithValue("@embedding", ConvertEmbeddingToBlob(chunk.Embedding));
                command.Parameters.AddWithValue("@indexed_at", chunk.IndexedAt.ToString("o"));
                command.Parameters.AddWithValue("@hash", chunk.Hash);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> DocumentExistsAsync(string documentName, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM document_chunks WHERE document_name = @document_name";
        command.Parameters.AddWithValue("@document_name", documentName);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    public async Task DeleteDocumentAsync(string documentName, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM document_chunks WHERE document_name = @document_name";
        command.Parameters.AddWithValue("@document_name", documentName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IEnumerable<DocumentChunk>> SearchSimilarAsync(
        float[] queryEmbedding,
        int maxResults,
        float similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // First, get all chunks
        var getAllCommand = connection.CreateCommand();
        getAllCommand.CommandText = @"
            SELECT 
                id,
                document_name,
                page_number,
                chunk_index,
                text,
                embedding,
                indexed_at,
                hash
            FROM document_chunks";

        var allChunks = new List<(DocumentChunk chunk, float similarity)>();
        
        using var reader = await getAllCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chunk = new DocumentChunk
            {
                Id = reader.GetInt32(0),
                DocumentName = reader.GetString(1),
                PageNumber = reader.GetInt32(2),
                ChunkIndex = reader.GetInt32(3),
                Text = reader.GetString(4),
                Embedding = ConvertBlobToEmbedding(reader.GetFieldValue<byte[]>(5)),
                IndexedAt = DateTime.Parse(reader.GetString(6)),
                Hash = reader.GetString(7)
            };

            // Calculate cosine similarity in C#
            var similarity = CalculateCosineSimilarity(queryEmbedding, chunk.Embedding);
            
            if (similarity >= similarityThreshold)
            {
                allChunks.Add((chunk, similarity));
            }
        }

        // Sort by similarity and take top results
        return allChunks
            .OrderByDescending(x => x.similarity)
            .Take(maxResults)
            .Select(x => x.chunk)
            .ToList();
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

    private byte[] ConvertEmbeddingToBlob(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private float[] ConvertBlobToEmbedding(byte[] blob)
    {
        var embedding = new float[blob.Length / sizeof(float)];
        Buffer.BlockCopy(blob, 0, embedding, 0, blob.Length);
        return embedding;
    }
} 