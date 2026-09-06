namespace PrRag.Application.Configuration;

public sealed class SkillsSettings
{
    public const string SectionName = "Skills";

    public string Directory { get; set; } = "/data/skills";
}