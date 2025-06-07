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
        _session = new InferenceSession(modelPath);
        _logger = logger;
        _dimension = _session.OutputMetadata.First().Value.Dimensions[1];
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            // Tokenize and prepare input
            var tokens = Tokenize(text);
            var seqLength = Math.Min(tokens.Length, 512); // Limit to model's max sequence length
            
            // Create input_ids tensor
            var inputTensor = new DenseTensor<long>(new[] { 1, seqLength });
            for (int i = 0; i < seqLength; i++)
            {
                inputTensor[0, i] = tokens[i];
            }

            // Create attention_mask tensor (1 for real tokens, 0 for padding)
            var attentionMask = new DenseTensor<long>(new[] { 1, seqLength });
            for (int i = 0; i < seqLength; i++)
            {
                attentionMask[0, i] = 1; // All tokens are real (no padding in this simple implementation)
            }

            // Create token_type_ids tensor (0 for all tokens in single segment)
            var tokenTypeIds = new DenseTensor<long>(new[] { 1, seqLength });
            for (int i = 0; i < seqLength; i++)
            {
                tokenTypeIds[0, i] = 0; // All tokens belong to segment 0
            }

            // Prepare input
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
            };

            // Run inference
            using var results = _session.Run(inputs);
            var embeddings = results.First().AsEnumerable<float>().ToArray();

            // Normalize the embedding
            var magnitude = Math.Sqrt(embeddings.Sum(x => x * x));
            return Task.FromResult(embeddings.Select(x => (float)(x / magnitude)).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding for text: {Text}", text);
            throw;
        }
    }

    private int[] Tokenize(string text)
    {
        // This is a simplified tokenization. In a real implementation,
        // you would use the model's specific tokenizer
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Select(w => w.GetHashCode() % 30000).ToArray(); // Simplified tokenization
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
} 