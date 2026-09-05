using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PrRag.Application.Abstractions;
using PrRag.Application.Domain;

namespace PrRag.Tests;

public sealed class FakeEmbeddingService : IEmbeddingService
{
    private readonly object _lock = new();
    private int _callCount;

    public int CallCount
    {
        get { lock (_lock) { return _callCount; } }
    }

    public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _callCount++;
        }

        return Task.FromResult(Embed(text));
    }

    public async Task<IReadOnlyList<float[]>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        var results = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            results.Add(await GenerateAsync(text, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// Produces deterministic vectors where texts sharing words are similar, so
    /// semantic retrieval is stable in tests (unlike pure random vectors, whose
    /// cosine similarities are unpredictable).
    /// </summary>
    private static float[] Embed(string text)
    {
        var vector = new float[PurchaseRequisition.EmbeddingDimensions];

        var words = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(w => w.Length > 0);

        foreach (var word in words)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(word));
            for (var i = 0; i < vector.Length; i += 8)
            {
                var bucket = (hash[(i / 8) % hash.Length] - 128f) / 128f;
                for (var j = 0; j < 8 && i + j < vector.Length; j++)
                {
                    vector[i + j] += bucket;
                }
            }
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
