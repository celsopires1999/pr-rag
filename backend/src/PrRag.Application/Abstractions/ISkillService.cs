using PrRag.Application.Domain;

namespace PrRag.Application.Abstractions;

public interface ISkillService
{
    /// <summary>Returns the current skill manifest (name + description) to expose to the chat model.</summary>
    IReadOnlyList<SkillManifestEntry> GetManifest();

    /// <summary>Returns the loaded skill with the given name, or null when not found.</summary>
    Task<Skill?> GetSkillAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Re-scans the skills directory and reloads the in-memory manifest.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}