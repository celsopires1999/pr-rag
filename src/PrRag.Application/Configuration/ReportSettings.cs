namespace PrRag.Application.Configuration;

public sealed class ReportSettings
{
    public const string SectionName = "RAG:Report";

    public string OutputDirectory { get; set; } = "./reports";
}
