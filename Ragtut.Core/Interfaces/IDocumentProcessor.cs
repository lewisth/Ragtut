using Ragtut.Core.Models;

namespace Ragtut.Core.Interfaces;

public interface IDocumentProcessor
{
    Task<IEnumerable<DocumentChunk>> ProcessDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    bool SupportsFileType(string filePath);
} 