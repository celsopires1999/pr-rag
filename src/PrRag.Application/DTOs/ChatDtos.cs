using System.Text.Json.Serialization;

namespace PrRag.Application.DTOs;

public sealed class ChatRequest
{
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("top_k")]
    public int TopK { get; set; }

    [JsonPropertyName("min_similarity")]
    public double MinSimilarity { get; set; }
}

public sealed class ChatResponse
{
    public string Answer { get; set; } = string.Empty;
    public int RetrievedCount { get; set; }
}
