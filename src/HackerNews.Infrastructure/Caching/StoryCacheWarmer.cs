using HackerNews.Application.Abstractions;
using HackerNews.Application.BestStories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure.Caching;

internal interface IStoryCacheWarmer
{
    Task WarmAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Pushes fresh data into the cache ahead of expiry so user requests are served from cache.
/// The ID list is refreshed on every run; items only once they are older than
/// <see cref="HackerNewsOptions.ItemRefreshInterval"/>, which keeps steady-state upstream load low.
/// </summary>
internal sealed partial class StoryCacheWarmer(
    IHackerNewsClient upstream,
    StoryCache cache,
    CacheWarmupState state,
    IOptions<HackerNewsOptions> hackerNewsOptions,
    IOptions<BestStoriesOptions> bestStoriesOptions,
    TimeProvider timeProvider,
    ILogger<StoryCacheWarmer> logger) : IStoryCacheWarmer
{
    public async Task WarmAsync(CancellationToken cancellationToken)
    {
        var ids = await upstream.GetBestStoryIdsAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var due = ids.Where(id => state.IsItemDue(id, now, hackerNewsOptions.Value.ItemRefreshInterval)).ToList();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = bestStoriesOptions.Value.MaxConcurrency,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(due, parallelOptions, async (id, ct) =>
        {
            try
            {
                var story = await upstream.GetStoryAsync(id, ct);
                await cache.SetStoryAsync(id, story, ct);
                state.MarkItemRefreshed(id, now);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Leave the item due; it is retried on the next run and readers fall back to stale data.
                LogItemRefreshFailed(ex, id.Value);
            }
        });

        // Publish IDs last so readers never see IDs whose items were not attempted yet.
        await cache.SetBestStoryIdsAsync(ids, cancellationToken);
        state.CompleteRefresh(ids, now);
        LogRefreshed(ids.Count, due.Count);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh item {ItemId}")]
    private partial void LogItemRefreshFailed(Exception exception, int itemId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refreshed best stories: {IdCount} IDs, {FetchedCount} items fetched")]
    private partial void LogRefreshed(int idCount, int fetchedCount);
}
