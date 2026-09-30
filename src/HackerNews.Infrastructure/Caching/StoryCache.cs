using System.Globalization;
using System.Text.Json;
using HackerNews.Application.Abstractions;
using HackerNews.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace HackerNews.Infrastructure.Caching;

/// <summary>
/// Story cache on top of <see cref="HybridCache"/>: per-key TTLs, single-flight upstream calls
/// (stampede protection), and a long-lived last-known-good copy served when upstream fails.
/// </summary>
internal sealed partial class StoryCache(HybridCache cache, IOptions<HackerNewsOptions> options, ILogger<StoryCache> logger)
{
    private const string BestStoriesKey = "hn:best-ids";
    private const string StalePrefix = "stale:";

    private static readonly HybridCacheEntryOptions _staleReadOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableUnderlyingData
              | HybridCacheEntryFlags.DisableLocalCacheWrite
              | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    private HackerNewsOptions Options => options.Value;

    public async Task<IReadOnlyList<StoryId>> GetBestStoryIdsAsync(
        Func<CancellationToken, Task<IReadOnlyList<StoryId>>> fetch,
        CancellationToken cancellationToken)
    {
        var entry = await GetOrFetchAsync(
            BestStoriesKey,
            async ct => CachedStoryIds.From(await fetch(ct)),
            Options.BestStoriesTtl,
            cancellationToken);
        return entry.ToStoryIds();
    }

    public Task SetBestStoryIdsAsync(IReadOnlyList<StoryId> ids, CancellationToken cancellationToken) =>
        SetAsync(BestStoriesKey, CachedStoryIds.From(ids), Options.BestStoriesTtl, cancellationToken);

    public async Task<Story?> GetStoryAsync(
        StoryId id,
        Func<CancellationToken, Task<Story?>> fetch,
        CancellationToken cancellationToken)
    {
        var entry = await GetOrFetchAsync(
            ItemKey(id),
            async ct => CachedItem.From(await fetch(ct)),
            Options.ItemTtl,
            cancellationToken);
        return entry.ToStory();
    }

    public Task SetStoryAsync(StoryId id, Story? story, CancellationToken cancellationToken) =>
        SetAsync(ItemKey(id), CachedItem.From(story), Options.ItemTtl, cancellationToken);

    private async Task<T> GetOrFetchAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> fetch,
        TimeSpan ttl,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            // HybridCache runs one factory per key; its token is cancelled only when every waiting caller has cancelled.
            return await cache.GetOrCreateAsync(
                key,
                (Cache: this, Key: key, Fetch: fetch),
                static async (state, ct) =>
                {
                    var value = await state.Fetch(ct);
                    await state.Cache.SetStaleAsync(state.Key, value, ct);
                    return value;
                },
                EntryOptions(ttl),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsUpstreamFailure(ex))
        {
            var stale = await cache.GetOrCreateAsync<T?>(
                StalePrefix + key,
                static _ => ValueTask.FromResult<T?>(null),
                _staleReadOptions,
                cancellationToken: cancellationToken);

            if (stale is null)
            {
                throw new HackerNewsUnavailableException("Hacker News is unavailable and no cached data exists.", ex);
            }

            LogServingStale(ex, key);
            return stale;
        }
    }

    private async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await cache.SetAsync(key, value, EntryOptions(ttl), cancellationToken: cancellationToken);
        await SetStaleAsync(key, value, cancellationToken);
    }

    private ValueTask SetStaleAsync<T>(string key, T value, CancellationToken cancellationToken) =>
        cache.SetAsync(StalePrefix + key, value, EntryOptions(Options.StaleTtl), cancellationToken: cancellationToken);

    // Network errors, non-success status codes, Polly timeouts/open circuit, HttpClient timeout, malformed payloads.
    private static bool IsUpstreamFailure(Exception ex) =>
        ex is HttpRequestException or ExecutionRejectedException or OperationCanceledException or JsonException;

    private static HybridCacheEntryOptions EntryOptions(TimeSpan ttl) => new() { Expiration = ttl, LocalCacheExpiration = ttl };

    private static string ItemKey(StoryId id) => string.Create(CultureInfo.InvariantCulture, $"hn:item:{id.Value}");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hacker News unavailable; serving stale cache entry {CacheKey}")]
    private partial void LogServingStale(Exception exception, string cacheKey);
}
