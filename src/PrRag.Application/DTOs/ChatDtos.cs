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

public sealed class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class ChatStreamRequest
{
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("top_k")]
    public int TopK { get; set; }

    [JsonPropertyName("min_similarity")]
    public double MinSimilarity { get; set; }

    public List<ChatMessageDto> Messages { get; set; } = new();
}
