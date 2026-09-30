using HackerNews.Application.Abstractions;
using HackerNews.Domain;

namespace HackerNews.Infrastructure.Caching;

/// <summary>Caching decorator: reads go through <see cref="StoryCache"/>, misses fall through to <paramref name="inner"/>.</summary>
internal sealed class CachingHackerNewsClient(IHackerNewsClient inner, StoryCache cache) : IHackerNewsClient
{
    public Task<IReadOnlyList<StoryId>> GetBestStoryIdsAsync(CancellationToken cancellationToken) =>
        cache.GetBestStoryIdsAsync(inner.GetBestStoryIdsAsync, cancellationToken);

    public Task<Story?> GetStoryAsync(StoryId id, CancellationToken cancellationToken) =>
        cache.GetStoryAsync(id, ct => inner.GetStoryAsync(id, ct), cancellationToken);
}
