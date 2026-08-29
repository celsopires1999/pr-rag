using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrRag.Application.Abstractions;
using PrRag.Application.Configuration;

namespace PrRag.Infrastructure.Services;

public sealed class FileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DataSettings> _dataSettings;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(5);
    private Timer? _debounceTimer;

    public FileWatcherService(
        IServiceScopeFactory scopeFactory,
        IOptions<DataSettings> dataSettings,
        ILogger<FileWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _dataSettings = dataSettings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var filePath = _dataSettings.Value.FilePath;
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Invalid data file path: {filePath}");
        var fileName = Path.GetFileName(fullPath);

        while (!stoppingToken.IsCancellationRequested && !File.Exists(fullPath))
        {
            _logger.LogInformation("Waiting for data file {Path} to appear...", fullPath);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;

        _logger.LogInformation("Watching {Folder} for changes to {File}.", directory, fileName);

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
        _debounceTimer = new Timer(async _ => await IngestAsync(), null, _debounce, Timeout.InfiniteTimeSpan);
    }

    private async Task IngestAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ingestionService = scope.ServiceProvider.GetRequiredService<IIngestionService>();
            var result = await ingestionService.IngestAsync();
            _logger.LogInformation(
                "Auto-ingest completed: {Total} records, {Ins} inserted, {Upd} updated, {Emb} embedded.",
                result.TotalRecords, result.Inserted, result.Updated, result.Embedded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-ingest failed.");
        }
    }
}
