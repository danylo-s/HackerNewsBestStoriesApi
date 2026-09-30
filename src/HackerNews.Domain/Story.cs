namespace HackerNews.Domain;

/// <summary>A live (not deleted, not dead) Hacker News story.</summary>
public sealed record Story
{
    private const string DiscussionUrlPrefix = "https://news.ycombinator.com/item?id=";

    public Story(StoryId id, string title, string? url, string postedBy, DateTimeOffset time, Score score, int commentCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(postedBy);
        ArgumentOutOfRangeException.ThrowIfNegative(commentCount);

        Id = id;
        Title = title;
        Uri = string.IsNullOrWhiteSpace(url) ? $"{DiscussionUrlPrefix}{id}" : url;
        PostedBy = postedBy;
        Time = time;
        Score = score;
        CommentCount = commentCount;
    }

    public StoryId Id { get; }

    public string Title { get; }

    /// <summary>External link, or the HN discussion page for text posts such as "Ask HN".</summary>
    public string Uri { get; }

    public string PostedBy { get; }

    public DateTimeOffset Time { get; }

    public Score Score { get; }

    public int CommentCount { get; }
}
