using System.Threading.Channels;
using HackerNews.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HackerNews.Infrastructure.Tests;

public sealed class BestStoriesRefreshServiceTests : IAsyncDisposable
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(45);

    private readonly FakeTimeProvider _time = new();
    private readonly Channel<int> _calls = Channel.CreateUnbounded<int>();
    private readonly IStoryCacheWarmer _warmer = Substitute.For<IStoryCacheWarmer>();
    private readonly ServiceProvider _provider;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private int _callCount;

    public BestStoriesRefreshServiceTests()
    {
        _warmer.WarmAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Writer.TryWrite(Interlocked.Increment(ref _callCount));
            return Task.CompletedTask;
        });
        var services = new ServiceCollection();
        services.AddScoped(_ => _warmer);
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Refreshes_on_start_and_then_every_interval()
    {
        using var service = CreateService(enabled: true);
        await service.StartAsync(_ct);

        (await NextCallAsync()).ShouldBe(1);
        _time.Advance(_interval);
        (await NextCallAsync()).ShouldBe(2);
        _time.Advance(_interval);
        (await NextCallAsync()).ShouldBe(3);

        await service.StopAsync(_ct);
    }

    [Fact]
    public async Task Keeps_running_after_a_failed_refresh()
    {
        _warmer.WarmAsync(Arg.Any<CancellationToken>()).Returns(
            _ =>
            {
                _calls.Writer.TryWrite(Interlocked.Increment(ref _callCount));
                return Task.FromException(new HttpRequestException("down"));
            },
            _ =>
            {
                _calls.Writer.TryWrite(Interlocked.Increment(ref _callCount));
                return Task.CompletedTask;
            });
        using var service = CreateService(enabled: true);
        await service.StartAsync(_ct);

        (await NextCallAsync()).ShouldBe(1);
        _time.Advance(_interval);
        (await NextCallAsync()).ShouldBe(2);

        await service.StopAsync(_ct);
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        using var service = CreateService(enabled: false);
        await service.StartAsync(_ct);
        _time.Advance(_interval * 3);
        await service.StopAsync(_ct);

        _callCount.ShouldBe(0);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private async Task<int> NextCallAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        return await _calls.Reader.ReadAsync(timeout.Token);
    }

    private BestStoriesRefreshService CreateService(bool enabled) =>
        new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HackerNewsOptions { RefreshEnabled = enabled, RefreshInterval = _interval }),
            _time,
            NullLogger<BestStoriesRefreshService>.Instance);
}
