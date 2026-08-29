namespace PrRag.Application.Configuration;

public sealed class DataSettings
{
    public const string SectionName = "Data";

    public string FilePath { get; set; } = "/data/purchase.json";
}
