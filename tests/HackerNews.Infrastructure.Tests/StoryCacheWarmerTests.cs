using HackerNews.Application.Abstractions;
using HackerNews.Application.BestStories;
using HackerNews.Domain;
using HackerNews.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HackerNews.Infrastructure.Tests;

public sealed class StoryCacheWarmerTests : IDisposable
{
    private static readonly TimeSpan _itemRefreshInterval = TimeSpan.FromMinutes(4);

    private readonly IHackerNewsClient _upstream = Substitute.For<IHackerNewsClient>();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2025-01-01T00:00:00Z", null));
    private readonly CacheWarmupState _state = new();
    private readonly ServiceProvider _provider;
    private readonly StoryCache _cache;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public StoryCacheWarmerTests()
    {
        _provider = TestServices.Build(options => options.ItemRefreshInterval = _itemRefreshInterval);
        _cache = _provider.GetRequiredService<StoryCache>();
        ArrangeUpstream(1, 2);
    }

    [Fact]
    public async Task Warm_populates_cache_so_reads_do_not_hit_upstream()
    {
        await CreateWarmer().WarmAsync(_ct);
        _upstream.ClearReceivedCalls();
        var reader = new CachingHackerNewsClient(_upstream, _cache);

        var ids = await reader.GetBestStoryIdsAsync(_ct);
        var story = await reader.GetStoryAsync(new StoryId(2), _ct);

        ids.ShouldBe([new StoryId(1), new StoryId(2)]);
        story.ShouldNotBeNull().Score.ShouldBe(new Score(20));
        _upstream.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Items_are_not_refetched_before_the_item_refresh_interval()
    {
        var warmer = CreateWarmer();
        await warmer.WarmAsync(_ct);
        _time.Advance(_itemRefreshInterval - TimeSpan.FromSeconds(1));

        await warmer.WarmAsync(_ct);

        await _upstream.Received(2).GetBestStoryIdsAsync(Arg.Any<CancellationToken>());
        await _upstream.Received(1).GetStoryAsync(new StoryId(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Items_are_refetched_once_the_item_refresh_interval_elapses()
    {
        var warmer = CreateWarmer();
        await warmer.WarmAsync(_ct);
        _time.Advance(_itemRefreshInterval);

        await warmer.WarmAsync(_ct);

        await _upstream.Received(2).GetStoryAsync(new StoryId(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_ids_are_fetched_on_the_next_refresh()
    {
        var warmer = CreateWarmer();
        await warmer.WarmAsync(_ct);
        ArrangeUpstream(1, 2, 3);

        await warmer.WarmAsync(_ct);

        await _upstream.Received(1).GetStoryAsync(new StoryId(3), Arg.Any<CancellationToken>());
        await _upstream.Received(1).GetStoryAsync(new StoryId(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_item_does_not_abort_the_refresh()
    {
        _upstream.GetStoryAsync(new StoryId(1), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Story?>(new HttpRequestException("boom")));

        await CreateWarmer().WarmAsync(_ct);

        _state.LastSuccessfulRefresh.ShouldBe(_time.GetUtcNow());
        var reader = new CachingHackerNewsClient(_upstream, _cache);
        (await reader.GetStoryAsync(new StoryId(2), _ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Successful_refresh_is_recorded()
    {
        _state.LastSuccessfulRefresh.ShouldBeNull();

        await CreateWarmer().WarmAsync(_ct);

        _state.LastSuccessfulRefresh.ShouldBe(_time.GetUtcNow());
    }

    [Fact]
    public async Task Id_list_failure_propagates_and_is_not_recorded_as_success()
    {
        _upstream.GetBestStoryIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<StoryId>>(new HttpRequestException("down")));

        await Should.ThrowAsync<HttpRequestException>(CreateWarmer().WarmAsync(_ct));

        _state.LastSuccessfulRefresh.ShouldBeNull();
    }

    public void Dispose() => _provider.Dispose();

    private void ArrangeUpstream(params int[] ids)
    {
        _upstream.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns(ids.Select(i => new StoryId(i)).ToList());
        foreach (var id in ids)
        {
            _upstream.GetStoryAsync(new StoryId(id), Arg.Any<CancellationToken>())
                .Returns(new Story(new StoryId(id), $"S{id}", null, "a", DateTimeOffset.UnixEpoch, new Score(id * 10), 0));
        }
    }

    private StoryCacheWarmer CreateWarmer() =>
        new(
            _upstream,
            _cache,
            _state,
            _provider.GetRequiredService<IOptions<HackerNewsOptions>>(),
            Options.Create(new BestStoriesOptions { MaxConcurrency = 4 }),
            _time,
            NullLogger<StoryCacheWarmer>.Instance);
}
