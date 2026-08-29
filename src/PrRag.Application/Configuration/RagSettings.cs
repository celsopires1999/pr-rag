namespace PrRag.Application.Configuration;

public sealed class RagSettings
{
    public const string SectionName = "RAG";

    public int TopK { get; set; } = 5;
    public double MinSimilarity { get; set; } = 0.7;
}
