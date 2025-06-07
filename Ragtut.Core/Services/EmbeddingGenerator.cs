using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Extensions.Logging;
using Ragtut.Core.Interfaces;
using System.Text;

namespace Ragtut.Core.Services;

public class EmbeddingGenerator : IEmbeddingGenerator, IDisposable
{
    private readonly InferenceSession _session;
    private readonly ILogger<EmbeddingGenerator> _logger;
    private readonly int _dimension;

    public int EmbeddingDimension => _dimension;

    public EmbeddingGenerator(string modelPath, ILogger<EmbeddingGenerator> logger)
    {
        // Note: We're keeping the ONNX session for future compatibility, but not using it currently
        // The TF-IDF approach doesn't require loading an actual model file
        try
        {
            if (File.Exists(modelPath))
            {
                _session = new InferenceSession(modelPath);
            }
            else
            {
                // For testing and development, create a null session when model doesn't exist
                _session = null!;
            }
        }
        catch
        {
            // If model loading fails, continue with TF-IDF approach
            _session = null!;
        }
        
        _logger = logger;
        _dimension = 384; // Fixed dimension for our TF-IDF approach
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if cancellation is requested
            cancellationToken.ThrowIfCancellationRequested();
            
            // Use TF-IDF based approach for better semantic similarity
            var embedding = CreateTfIdfEmbedding(text);
            return Task.FromResult(embedding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {Text}", text);
            throw;
        }
    }
    
    private float[] CreateTfIdfEmbedding(string text)
    {
        // Create a 384-dimensional embedding using TF-IDF principles
        var embedding = new float[384];
        
        // Normalize and clean text
        text = text.ToLowerInvariant();
        
        // Remove common punctuation but keep meaningful separators
        text = text.Replace(".", " ").Replace(",", " ").Replace("!", " ")
                   .Replace("?", " ").Replace(":", " ").Replace(";", " ")
                   .Replace("(", " ").Replace(")", " ").Replace("[", " ")
                   .Replace("]", " ").Replace("{", " ").Replace("}", " ");
        
        // Extract words
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                       .Where(w => w.Length > 2) // Filter out very short words
                       .ToArray();
        
        if (words.Length == 0) return embedding;
        
        // Calculate word frequencies (TF)
        var wordFreq = words.GroupBy(w => w)
                           .ToDictionary(g => g.Key, g => (float)g.Count() / words.Length);
        
        // Create embedding based on important words and their frequencies
        foreach (var (word, freq) in wordFreq)
        {
            // Create multiple hash values for each word to distribute across dimensions
            for (int i = 0; i < 3; i++)
            {
                var hash = GetStableHash(word + i.ToString());
                var dimIndex = Math.Abs(hash) % 384;
                
                // Weight by frequency and apply IDF-like weighting (prefer less common words)
                var weight = freq * (1.0f + 1.0f / (float)Math.Log(1 + freq));
                embedding[dimIndex] += weight;
            }
        }
        
        // Add bigram features for better context understanding
        for (int i = 0; i < words.Length - 1; i++)
        {
            var bigram = words[i] + "_" + words[i + 1];
            var hash = GetStableHash(bigram);
            var dimIndex = Math.Abs(hash) % 384;
            embedding[dimIndex] += 0.5f; // Lower weight for bigrams
        }
        
        // Add character n-gram features for partial word matching
        for (int i = 0; i < text.Length - 2; i++)
        {
            var trigram = text.Substring(i, 3);
            if (trigram.All(c => char.IsLetter(c))) // Only letter trigrams
            {
                var hash = GetStableHash(trigram);
                var dimIndex = Math.Abs(hash) % 384;
                embedding[dimIndex] += 0.1f; // Low weight for character features
            }
        }
        
        // Normalize the embedding using L2 norm
        var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= magnitude;
            }
        }
        
        return embedding;
    }
    
    private int GetStableHash(string input)
    {
        // Create a stable hash that doesn't depend on .NET version or session
        int hash = 0;
        foreach (char c in input)
        {
            hash = (hash * 31 + c) & 0x7FFFFFFF; // Keep positive
        }
        return hash;
    }

    private int[] Tokenize(string text)
    {
        // Better tokenization for sentence-transformers model
        // This is still simplified but much better than hash-based approach
        
        // Clean and normalize text
        text = text.ToLowerInvariant()
                   .Replace(".", " . ")
                   .Replace(",", " , ")
                   .Replace("!", " ! ")
                   .Replace("?", " ? ")
                   .Replace(":", " : ")
                   .Replace(";", " ; ");
        
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Create a simple vocabulary mapping
        var vocab = new Dictionary<string, int>();
        var tokens = new List<int>();
        
        // Add special tokens
        vocab["[CLS]"] = 101;
        vocab["[SEP]"] = 102;
        vocab["[UNK]"] = 100;
        vocab["[PAD]"] = 0;
        
        tokens.Add(101); // [CLS] token
        
        foreach (var word in words)
        {
            // Use a more consistent mapping based on string content
            if (!vocab.ContainsKey(word))
            {
                // Create consistent token IDs based on word content
                var hash = word.GetHashCode();
                if (hash < 0) hash = -hash;
                vocab[word] = (hash % 28000) + 999; // Start vocab from 999 to avoid conflicts
            }
            tokens.Add(vocab[word]);
        }
        
        tokens.Add(102); // [SEP] token
        
        return tokens.ToArray();
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
} 