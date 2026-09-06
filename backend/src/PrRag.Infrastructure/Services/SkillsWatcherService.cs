using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;

namespace PrRag.Infrastructure.Services;

/// <summary>
/// Watches the skills directory and triggers a debounced reload of the skill
/// manifest when skill files are added, edited, renamed, or removed. Mirrors
/// the wait-for-content pattern of <see cref="FileWatcherService"/>.
/// </summary>
public sealed class SkillsWatcherService : BackgroundService
{
    private readonly ISkillService _skills;
    private readonly IOptions<SkillsSettings> _skillsSettings;
    private readonly ILogger<SkillsWatcherService> _logger;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(5);
    private Timer? _debounceTimer;

    public SkillsWatcherService(
        ISkillService skills,
        IOptions<SkillsSettings> skillsSettings,
        ILogger<SkillsWatcherService> logger)
    {
        _skills = skills;
        _skillsSettings = skillsSettings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = Path.GetFullPath(_skillsSettings.Value.Directory);

        while (!stoppingToken.IsCancellationRequested && !Directory.Exists(directory))
        {
            _logger.LogInformation("Waiting for skills directory {Directory} to appear...", directory);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var watcher = new FileSystemWatcher(directory, "*.md")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Deleted += OnChanged;

        _logger.LogInformation("Watching {Directory} for skill changes.", directory);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
            _debounceTimer?.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(async _ => await ReloadSkillsAsync(), null, _debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task ReloadSkillsAsync()
    {
        try
        {
            await _skills.ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload skills.");
        }
    }
}