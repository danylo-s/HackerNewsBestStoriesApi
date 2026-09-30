using HackerNews.Domain;

namespace HackerNews.Application.Tests;

internal static class StoryFactory
{
    public static Story Create(int id, int score) =>
        new(
            new StoryId(id),
            $"Story {id}",
            $"https://example.com/{id}",
            $"user{id}",
            DateTimeOffset.UnixEpoch.AddSeconds(id),
            new Score(score),
            id * 2);
}
