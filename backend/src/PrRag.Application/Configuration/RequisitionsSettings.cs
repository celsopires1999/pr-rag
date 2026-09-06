namespace PrRag.Application.Configuration;

public sealed class RequisitionsSettings
{
    public const string SectionName = "Requisitions";

    public string Directory { get; set; } = "./requisitions";
}