using HackerNews.Application.Abstractions;
using HackerNews.Application.BestStories;
using HackerNews.Domain;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.Tests;

public sealed class GetBestStoriesQueryHandlerTests
{
    private readonly IHackerNewsClient _client = Substitute.For<IHackerNewsClient>();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_stories_ordered_by_score_descending()
    {
        Arrange(StoryFactory.Create(1, 10), StoryFactory.Create(2, 30), StoryFactory.Create(3, 20));

        var result = await CreateHandler().Handle(new GetBestStoriesQuery(3), _ct);

        result.Select(s => s.Score).ShouldBe([30, 20, 10]);
    }

    [Fact]
    public async Task Returns_only_the_requested_number_of_stories()
    {
        Arrange(StoryFactory.Create(1, 10), StoryFactory.Create(2, 30), StoryFactory.Create(3, 20));

        var result = await CreateHandler().Handle(new GetBestStoriesQuery(2), _ct);

        result.Select(s => s.Title).ShouldBe(["Story 2", "Story 3"]);
    }

    [Fact]
    public async Task Returns_all_available_when_count_exceeds_them()
    {
        Arrange(StoryFactory.Create(1, 10));

        var result = await CreateHandler().Handle(new GetBestStoriesQuery(50), _ct);

        result.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Skips_items_the_client_could_not_resolve()
    {
        _client.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns([new StoryId(1), new StoryId(2)]);
        _client.GetStoryAsync(new StoryId(1), Arg.Any<CancellationToken>()).Returns((Story?)null);
        _client.GetStoryAsync(new StoryId(2), Arg.Any<CancellationToken>()).Returns(StoryFactory.Create(2, 5));

        var result = await CreateHandler().Handle(new GetBestStoriesQuery(10), _ct);

        result.ShouldHaveSingleItem().Title.ShouldBe("Story 2");
    }

    [Fact]
    public async Task Equal_scores_are_ordered_by_id_for_stable_output()
    {
        Arrange(StoryFactory.Create(9, 10), StoryFactory.Create(4, 10));

        var result = await CreateHandler().Handle(new GetBestStoriesQuery(2), _ct);

        result.Select(s => s.Title).ShouldBe(["Story 4", "Story 9"]);
    }

    [Fact]
    public async Task Maps_story_to_dto()
    {
        var story = StoryFactory.Create(7, 42);
        Arrange(story);

        var dto = (await CreateHandler().Handle(new GetBestStoriesQuery(1), _ct)).ShouldHaveSingleItem();

        dto.ShouldBe(new StoryDto(story.Title, story.Uri, story.PostedBy, story.Time, 42, story.CommentCount));
    }

    [Fact]
    public async Task Fetches_items_with_bounded_concurrency()
    {
        var ids = Enumerable.Range(1, 20).Select(i => new StoryId(i)).ToList();
        _client.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns(ids);
        var inFlight = 0;
        var maxInFlight = 0;
        _client.GetStoryAsync(Arg.Any<StoryId>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var current = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, current);
            await Task.Delay(10, _ct);
            Interlocked.Decrement(ref inFlight);
            return (Story?)StoryFactory.Create(call.Arg<StoryId>().Value, 1);
        });

        var result = await CreateHandler(maxConcurrency: 3).Handle(new GetBestStoriesQuery(20), _ct);

        result.Count.ShouldBe(20);
        maxInFlight.ShouldBeInRange(1, 3);
    }

    [Fact]
    public async Task Honours_cancellation()
    {
        Arrange(StoryFactory.Create(1, 10));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            CreateHandler().Handle(new GetBestStoriesQuery(1), cts.Token).AsTask());
    }

    private void Arrange(params Story[] stories)
    {
        _client.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns(stories.Select(s => s.Id).ToList());
        foreach (var story in stories)
        {
            _client.GetStoryAsync(story.Id, Arg.Any<CancellationToken>()).Returns(story);
        }
    }

    private GetBestStoriesQueryHandler CreateHandler(int maxConcurrency = 4) =>
        new(_client, Options.Create(new BestStoriesOptions { MaxConcurrency = maxConcurrency }));

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
