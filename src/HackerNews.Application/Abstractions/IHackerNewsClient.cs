using HackerNews.Domain;

namespace HackerNews.Application.Abstractions;

/// <summary>Port to the Hacker News data source.</summary>
public interface IHackerNewsClient
{
    Task<IReadOnlyList<StoryId>> GetBestStoryIdsAsync(CancellationToken cancellationToken);

    /// <summary>Returns <c>null</c> when the item is missing, deleted, dead or not a story.</summary>
    Task<Story?> GetStoryAsync(StoryId id, CancellationToken cancellationToken);
}
