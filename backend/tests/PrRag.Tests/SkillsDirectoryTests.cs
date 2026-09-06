using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrRag.Application.Configuration;
using PrRag.Infrastructure.Services;
using Xunit;

namespace PrRag.Tests;

public class SkillsDirectoryTests : IDisposable
{
    private readonly string _dir;
    private readonly SkillsDirectory _service;

    public SkillsDirectoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"prrag-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _service = new SkillsDirectory(
            Options.Create(new SkillsSettings { Directory = _dir }),
            NullLogger<SkillsDirectory>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task Valid_skill_files_are_parsed_into_the_manifest()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "create-purchase-requisition.md"), """
            ---
            name: create-purchase-requisition
            description: Use when creating a purchase requisition.
            version: 1
            ---
            # Role
            You act as a purchasing assistant.
            """);

        var entry = Assert.Single(_service.GetManifest());
        Assert.Equal("create-purchase-requisition", entry.Name);
        Assert.Contains("creating a purchase requisition", entry.Description);

        var skill = await _service.GetSkillAsync("create-purchase-requisition");
        Assert.NotNull(skill);
        Assert.Equal(1, skill.Version);
        Assert.Contains("purchasing assistant", skill.Body);
    }

    [Fact]
    public void Files_missing_required_front_matter_are_skipped()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.md"), "# No front matter");
        File.WriteAllText(Path.Combine(_dir, "no-version.md"), """
            ---
            name: no-version
            description: Missing version.
            ---
            body content
            """);

        Assert.Empty(_service.GetManifest());
    }

    [Fact]
    public void Missing_or_empty_directory_yields_empty_manifest()
    {
        Directory.Delete(_dir, recursive: true);

        Assert.Empty(_service.GetManifest());
    }

    [Fact]
    public async Task Reload_picks_up_new_skill_files()
    {
        Assert.Empty(_service.GetManifest());

        await File.WriteAllTextAsync(Path.Combine(_dir, "new-skill.md"), """
            ---
            name: new-skill
            description: A newly added skill.
            version: 1
            ---
            guidance body
            """);
        await _service.ReloadAsync();

        var entry = Assert.Single(_service.GetManifest());
        Assert.Equal("new-skill", entry.Name);
    }
}