namespace PrRag.Tests;

/// <summary>
/// Locates the files that ship as prompt input, so a guard reads the real thing
/// rather than a fixture. Shared because two separate defects were the same
/// defect in two places: prompt text naming a tool the running agent does not
/// own, once in <c>data/skills</c> and once in an action block.
/// </summary>
internal static class RepoFiles
{
    /// <summary>The repo's <c>data/skills</c> directory, found by walking up from the test output.</summary>
    public static string SkillsDir { get; } = FindSkillsDir();

    private static string FindSkillsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "skills");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find data/skills above {AppContext.BaseDirectory}.");
    }
}
