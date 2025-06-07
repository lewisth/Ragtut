namespace Ragtut.Core.Models;

public class DocumentChunk
{
    public int Id { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime IndexedAt { get; set; }
    public string Hash { get; set; } = string.Empty; // For duplicate detection
} 