using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;
using PrRag.Application.Domain;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Loads skills as markdown files (with YAML front matter) from the configured
/// directory into an in-memory manifest. Missing or empty directories produce an
/// empty manifest; files with missing/invalid front matter are skipped with a
/// log warning. Reload is pull-based (<see cref="ReloadAsync"/>); a
/// <see cref="SkillsWatcherService"/> watches the directory for changes.
/// </summary>
public sealed class SkillsDirectory : ISkillService
{
    private readonly object _lock = new();
    private readonly ILogger<SkillsDirectory> _logger;
    private readonly string _directory;
    private IReadOnlyDictionary<string, Skill> _skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public SkillsDirectory(IOptions<SkillsSettings> settings, ILogger<SkillsDirectory> logger)
    {
        _directory = settings.Value.Directory;
        _logger = logger;
    }

    public IReadOnlyList<SkillManifestEntry> GetManifest()
    {
        EnsureLoaded();
        lock (_lock)
        {
            return _skills.Values
                .OrderBy(s => s.Name)
                .Select(s => new SkillManifestEntry(s.Name, s.Description))
                .ToList();
        }
    }

    public Task<Skill?> GetSkillAsync(string name, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        lock (_lock)
        {
            return Task.FromResult(_skills.TryGetValue(name, out var skill) ? skill : null);
        }
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        Scan();
        return Task.CompletedTask;
    }

    private void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded))
        {
            return;
        }

        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }

            Scan();
            _loaded = true;
        }
    }

    private void Scan()
    {
        try
        {
            var fullPath = Path.GetFullPath(_directory);
            var loaded = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);

            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath, "*.md", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var skill = SkillMarkdownParser.Parse(File.ReadAllText(file));
                        if (skill is null)
                        {
                            _logger.LogWarning(
                                "Skipping skill file {File}: missing required front matter (name, description, version) or empty guidance.",
                                file);
                            continue;
                        }

                        loaded[skill.Name] = skill;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load skill file {File}.", file);
                    }
                }
            }

            lock (_lock)
            {
                _skills = loaded;
            }

            _logger.LogInformation("Loaded {Count} skill(s) from {Directory}.", loaded.Count, fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan skills directory {Directory}.", _directory);
        }
    }
}