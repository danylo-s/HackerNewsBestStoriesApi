using HackerNews.Domain;

namespace HackerNews.Application.BestStories;

public sealed record StoryDto(string Title, string Uri, string PostedBy, DateTimeOffset Time, int Score, int CommentCount)
{
    internal static StoryDto From(Story story) =>
        new(story.Title, story.Uri, story.PostedBy, story.Time, story.Score.Value, story.CommentCount);
}
