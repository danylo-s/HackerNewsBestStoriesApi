using HackerNews.Domain;

namespace HackerNews.Infrastructure.HackerNewsApi;

internal static class HnItemMapper
{
    private const string StoryType = "story";

    /// <summary>Maps a raw item to a <see cref="Story"/>, or <c>null</c> if it is not a live, complete story.</summary>
    public static Story? ToStory(HnItem? item)
    {
        if (item is null
            || item.Deleted
            || item.Dead
            || item.Id <= 0
            || !string.Equals(item.Type, StoryType, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(item.Title)
            || string.IsNullOrWhiteSpace(item.By)
            || item.Time is not >= 0)
        {
            return null;
        }

        return new Story(
            new StoryId(item.Id),
            item.Title,
            item.Url,
            item.By,
            UnixTime.ToDateTimeOffset(item.Time.Value),
            new Score(Math.Max(item.Score ?? 0, 0)),
            Math.Max(item.Descendants ?? 0, 0));
    }
}
