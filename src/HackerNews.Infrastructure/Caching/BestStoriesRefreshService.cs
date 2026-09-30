using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Caching;

/// <summary>Runs <see cref="IStoryCacheWarmer"/> at startup and then every <see cref="HackerNewsOptions.RefreshInterval"/>.</summary>
internal sealed partial class BestStoriesRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<HackerNewsOptions> options,
    TimeProvider timeProvider,
    ILogger<BestStoriesRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RefreshEnabled)
        {
            LogDisabled();
            return;
        }

        using var timer = new PeriodicTimer(options.Value.RefreshInterval, timeProvider);
        try
        {
            do
            {
                await RefreshOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }

    private async Task RefreshOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // New scope per run: typed HttpClients must not be captured for the lifetime of a singleton.
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IStoryCacheWarmer>().WarmAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogRefreshFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Background refresh of best stories is disabled")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Background refresh of best stories failed; serving cached data")]
    private partial void LogRefreshFailed(Exception exception);
}
