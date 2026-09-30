using System.ComponentModel;
using HackerNews.Domain;

namespace HackerNews.Infrastructure.Caching;

// Serializable cache shapes. [ImmutableObject(true)] + sealed lets HybridCache hand out the
// same L1 instance instead of deserializing a copy on every hit.

[ImmutableObject(true)]
internal sealed record CachedStoryIds(int[] Ids)
{
    public static CachedStoryIds From(IReadOnlyList<StoryId> ids) => new([.. ids.Select(id => id.Value)]);

    public IReadOnlyList<StoryId> ToStoryIds() => [.. Ids.Select(id => new StoryId(id))];
}

/// <summary>Wraps a possibly missing story so that "not a story" results are cached too.</summary>
[ImmutableObject(true)]
internal sealed record CachedItem(CachedStory? Story)
{
    public static CachedItem From(Story? story) => new(story is null ? null : CachedStory.From(story));

    public Story? ToStory() => Story?.ToStory();
}

[ImmutableObject(true)]
internal sealed record CachedStory(int Id, string Title, string Uri, string PostedBy, DateTimeOffset Time, int Score, int CommentCount)
{
    public static CachedStory From(Story story) =>
        new(story.Id.Value, story.Title, story.Uri, story.PostedBy, story.Time, story.Score.Value, story.CommentCount);

    public Story ToStory() => new(new StoryId(Id), Title, Uri, PostedBy, Time, new Score(Score), CommentCount);
}
