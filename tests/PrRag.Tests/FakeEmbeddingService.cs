using System.Security.Cryptography;
using System.Text;
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

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vector = new float[PurchaseRequisition.EmbeddingDimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (hash[i % hash.Length] - 128f) / 128f;
        }

        return Task.FromResult(vector);
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
}
