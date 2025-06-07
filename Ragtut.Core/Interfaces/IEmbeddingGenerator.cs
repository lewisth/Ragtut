namespace Ragtut.Core.Interfaces;

public interface IEmbeddingGenerator
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    int EmbeddingDimension { get; }
} 