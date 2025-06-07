using Ragtut.Core.Models;

namespace Ragtut.Core.Interfaces;

public interface IVectorStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StoreChunksAsync(IEnumerable<DocumentChunk> chunks, CancellationToken cancellationToken = default);
    Task<bool> DocumentExistsAsync(string documentName, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(string documentName, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentChunk>> SearchSimilarAsync(float[] queryEmbedding, int maxResults, float similarityThreshold, CancellationToken cancellationToken = default);
} 