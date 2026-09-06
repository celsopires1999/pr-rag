namespace PrRag.Application.Domain;

public sealed record Skill(string Name, string Description, int Version, string Body);

public sealed record SkillManifestEntry(string Name, string Description);