using System.Collections.Concurrent;
using HackerNews.Domain;

namespace HackerNews.Infrastructure.Caching;

/// <summary>Singleton tracking what the background refresh has done; also drives readiness.</summary>
public sealed class CacheWarmupState
{
    private readonly ConcurrentDictionary<StoryId, DateTimeOffset> _itemRefreshedAt = new();
    private long _lastSuccessTicks;

    public DateTimeOffset? LastSuccessfulRefresh
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    internal bool IsItemDue(StoryId id, DateTimeOffset now, TimeSpan refreshInterval) =>
        !_itemRefreshedAt.TryGetValue(id, out var refreshedAt) || now - refreshedAt >= refreshInterval;

    internal void MarkItemRefreshed(StoryId id, DateTimeOffset at) => _itemRefreshedAt[id] = at;

    internal void CompleteRefresh(IReadOnlyCollection<StoryId> currentIds, DateTimeOffset at)
    {
        var current = currentIds.ToHashSet();
        foreach (var id in _itemRefreshedAt.Keys.Where(id => !current.Contains(id)))
        {
            _itemRefreshedAt.TryRemove(id, out _);
        }

        Interlocked.Exchange(ref _lastSuccessTicks, at.UtcTicks);
    }
}
