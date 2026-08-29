using Microsoft.Extensions.AI;
using PrRag.Application.Abstractions;

namespace PrRag.Infrastructure.Embeddings;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private const int BatchSize = 128;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public OpenAiEmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<float[]> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync([text], cancellationToken: cancellationToken);
        return embeddings[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>(texts.Count);

        for (var i = 0; i < texts.Count; i += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = new string[Math.Min(BatchSize, texts.Count - i)];
            for (var j = 0; j < chunk.Length; j++)
            {
                chunk[j] = texts[i + j];
            }

            var embeddings = await _embeddingGenerator.GenerateAsync(chunk, cancellationToken: cancellationToken);
            foreach (var embedding in embeddings)
            {
                results.Add(embedding.Vector.ToArray());
            }
        }

        return results;
    }
}
