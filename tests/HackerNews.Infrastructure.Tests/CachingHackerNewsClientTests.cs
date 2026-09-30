using HackerNews.Application.Abstractions;
using HackerNews.Domain;
using HackerNews.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace HackerNews.Infrastructure.Tests;

public sealed class CachingHackerNewsClientTests : IDisposable
{
    private static readonly StoryId _id = new(7);
    private static readonly Story _story = new(_id, "Title", "https://example.com", "author", DateTimeOffset.UnixEpoch, new Score(3), 1);

    private readonly IHackerNewsClient _upstream = Substitute.For<IHackerNewsClient>();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private ServiceProvider? _provider;

    [Fact]
    public async Task Second_id_lookup_is_served_from_cache()
    {
        _upstream.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns([_id]);
        var client = CreateClient();

        await client.GetBestStoryIdsAsync(_ct);
        var ids = await client.GetBestStoryIdsAsync(_ct);

        ids.ShouldBe([_id]);
        await _upstream.Received(1).GetBestStoryIdsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_item_lookup_is_served_from_cache()
    {
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>()).Returns(_story);
        var client = CreateClient();

        await client.GetStoryAsync(_id, _ct);
        var story = await client.GetStoryAsync(_id, _ct);

        story.ShouldBe(_story);
        await _upstream.Received(1).GetStoryAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_items_are_cached_too()
    {
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>()).Returns((Story?)null);
        var client = CreateClient();

        (await client.GetStoryAsync(_id, _ct)).ShouldBeNull();
        (await client.GetStoryAsync(_id, _ct)).ShouldBeNull();

        await _upstream.Received(1).GetStoryAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Concurrent_lookups_for_the_same_key_hit_upstream_once()
    {
        var gate = new TaskCompletionSource<Story?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var client = CreateClient();

        var lookups = Enumerable.Range(0, 50).Select(_ => client.GetStoryAsync(_id, _ct)).ToList();
        gate.SetResult(_story);
        var results = await Task.WhenAll(lookups);

        results.ShouldAllBe(s => s == _story);
        await _upstream.Received(1).GetStoryAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_value_is_served_when_upstream_fails_after_expiry()
    {
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Story?>(_story), Task.FromException<Story?>(new HttpRequestException("down")));
        var client = CreateClient(ttl: TimeSpan.FromMilliseconds(50));

        await client.GetStoryAsync(_id, _ct);
        await Task.Delay(200, _ct);
        var story = await client.GetStoryAsync(_id, _ct);

        story.ShouldBe(_story);
        await _upstream.Received(2).GetStoryAsync(_id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upstream_failure_without_stale_value_is_reported_as_unavailable()
    {
        _upstream.GetBestStoryIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<StoryId>>(new HttpRequestException("down")));

        var exception = await Should.ThrowAsync<HackerNewsUnavailableException>(CreateClient().GetBestStoryIdsAsync(_ct));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Non_upstream_failures_are_not_masked()
    {
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Story?>(_story), Task.FromException<Story?>(new InvalidOperationException("bug")));
        var client = CreateClient(ttl: TimeSpan.FromMilliseconds(50));

        await client.GetStoryAsync(_id, _ct);
        await Task.Delay(200, _ct);

        await Should.ThrowAsync<InvalidOperationException>(client.GetStoryAsync(_id, _ct));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_masked_by_stale_fallback()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(CreateClient().GetStoryAsync(_id, cts.Token));
    }

    [Fact]
    public async Task One_caller_cancelling_does_not_cancel_the_shared_fetch_for_others()
    {
        var gate = new TaskCompletionSource<Story?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _upstream.GetStoryAsync(_id, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var client = CreateClient();
        using var impatient = new CancellationTokenSource();

        var cancelled = client.GetStoryAsync(_id, impatient.Token);
        var patient = client.GetStoryAsync(_id, _ct);
        await impatient.CancelAsync();
        gate.SetResult(_story);

        await Should.ThrowAsync<OperationCanceledException>(cancelled);
        (await patient).ShouldBe(_story);
        await _upstream.Received(1).GetStoryAsync(_id, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _provider?.Dispose();

    private CachingHackerNewsClient CreateClient(TimeSpan? ttl = null)
    {
        _provider = TestServices.Build(options =>
        {
            options.ItemTtl = ttl ?? TimeSpan.FromMinutes(5);
            options.BestStoriesTtl = ttl ?? TimeSpan.FromMinutes(1);
            options.ItemRefreshInterval = TimeSpan.FromMilliseconds(10);
            options.RefreshInterval = TimeSpan.FromMilliseconds(10);
        });
        return new CachingHackerNewsClient(_upstream, _provider.GetRequiredService<StoryCache>());
    }
}
