namespace HackerNews.Domain.Tests;

public sealed class StoryTests
{
    private static readonly DateTimeOffset _time = new(2019, 10, 12, 13, 43, 1, TimeSpan.Zero);

    [Fact]
    public void Create_keeps_all_values()
    {
        var story = CreateStory(url: "https://example.com/a");

        story.Id.ShouldBe(new StoryId(21233041));
        story.Title.ShouldBe("A title");
        story.Uri.ShouldBe("https://example.com/a");
        story.PostedBy.ShouldBe("ismaildonmez");
        story.Time.ShouldBe(_time);
        story.Score.ShouldBe(new Score(1716));
        story.CommentCount.ShouldBe(572);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Story_without_external_url_links_to_its_hn_discussion(string? url)
    {
        var story = CreateStory(url: url);

        story.Uri.ShouldBe("https://news.ycombinator.com/item?id=21233041");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Title_is_required(string title)
    {
        Should.Throw<ArgumentException>(() => CreateStory(title: title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Author_is_required(string postedBy)
    {
        Should.Throw<ArgumentException>(() => CreateStory(postedBy: postedBy));
    }

    [Fact]
    public void Comment_count_cannot_be_negative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreateStory(commentCount: -1));
    }

    private static Story CreateStory(
        string title = "A title",
        string? url = "https://example.com",
        string postedBy = "ismaildonmez",
        int commentCount = 572) =>
        new(new StoryId(21233041), title, url, postedBy, _time, new Score(1716), commentCount);
}
