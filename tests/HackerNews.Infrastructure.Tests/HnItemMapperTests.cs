using HackerNews.Domain;
using HackerNews.Infrastructure.HackerNewsApi;

namespace HackerNews.Infrastructure.Tests;

public sealed class HnItemMapperTests
{
    private static readonly HnItem _valid = new(
        Id: 21233041,
        Type: "story",
        By: "ismaildonmez",
        Title: "A uBlock Origin update was rejected from the Chrome Web Store",
        Url: "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
        Score: 1716,
        Time: 1570887781,
        Descendants: 572,
        Deleted: false,
        Dead: false);

    [Fact]
    public void Maps_all_fields()
    {
        var story = HnItemMapper.ToStory(_valid).ShouldNotBeNull();

        story.Id.ShouldBe(new StoryId(21233041));
        story.Title.ShouldBe(_valid.Title);
        story.Uri.ShouldBe(_valid.Url);
        story.PostedBy.ShouldBe("ismaildonmez");
        story.Time.ShouldBe(new DateTimeOffset(2019, 10, 12, 13, 43, 1, TimeSpan.Zero));
        story.Score.ShouldBe(new Score(1716));
        story.CommentCount.ShouldBe(572);
    }

    [Fact]
    public void Null_item_maps_to_null() => HnItemMapper.ToStory(null).ShouldBeNull();

    [Fact]
    public void Deleted_item_is_skipped() => HnItemMapper.ToStory(_valid with { Deleted = true }).ShouldBeNull();

    [Fact]
    public void Dead_item_is_skipped() => HnItemMapper.ToStory(_valid with { Dead = true }).ShouldBeNull();

    [Theory]
    [InlineData("job")]
    [InlineData("comment")]
    [InlineData("poll")]
    [InlineData(null)]
    public void Non_story_items_are_skipped(string? type) =>
        HnItemMapper.ToStory(_valid with { Type = type }).ShouldBeNull();

    [Fact]
    public void Item_without_title_is_skipped() => HnItemMapper.ToStory(_valid with { Title = null }).ShouldBeNull();

    [Fact]
    public void Item_without_author_is_skipped() => HnItemMapper.ToStory(_valid with { By = null }).ShouldBeNull();

    [Fact]
    public void Item_without_time_is_skipped() => HnItemMapper.ToStory(_valid with { Time = null }).ShouldBeNull();

    [Fact]
    public void Ask_hn_without_url_links_to_discussion()
    {
        var story = HnItemMapper.ToStory(_valid with { Url = null }).ShouldNotBeNull();

        story.Uri.ShouldBe("https://news.ycombinator.com/item?id=21233041");
    }

    [Fact]
    public void Missing_score_and_descendants_default_to_zero()
    {
        var story = HnItemMapper.ToStory(_valid with { Score = null, Descendants = null }).ShouldNotBeNull();

        story.Score.ShouldBe(new Score(0));
        story.CommentCount.ShouldBe(0);
    }
}
