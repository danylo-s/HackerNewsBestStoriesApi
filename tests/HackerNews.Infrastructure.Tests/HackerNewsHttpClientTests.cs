using HackerNews.Domain;
using HackerNews.Infrastructure.HackerNewsApi;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HackerNews.Infrastructure.Tests;

public sealed class HackerNewsHttpClientTests : IDisposable
{
    private const string ItemJson =
        """
        {"by":"ismaildonmez","descendants":572,"id":21233041,"kids":[1,2],"score":1716,"time":1570887781,
         "title":"A uBlock Origin update was rejected from the Chrome Web Store","type":"story",
         "url":"https://github.com/uBlockOrigin/uBlock-issues/issues/745"}
        """;

    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gets_best_story_ids()
    {
        Stub("/v0/beststories.json", 200, "[3,1,2]");

        var ids = await CreateClient().GetBestStoryIdsAsync(_ct);

        ids.ShouldBe([new StoryId(3), new StoryId(1), new StoryId(2)]);
    }

    [Fact]
    public async Task Gets_and_maps_a_story()
    {
        Stub("/v0/item/21233041.json", 200, ItemJson);

        var story = await CreateClient().GetStoryAsync(new StoryId(21233041), _ct);

        story.ShouldNotBeNull();
        story.Title.ShouldBe("A uBlock Origin update was rejected from the Chrome Web Store");
        story.Score.ShouldBe(new Score(1716));
        story.CommentCount.ShouldBe(572);
        story.PostedBy.ShouldBe("ismaildonmez");
    }

    [Fact]
    public async Task Json_null_item_returns_null()
    {
        Stub("/v0/item/5.json", 200, "null");

        (await CreateClient().GetStoryAsync(new StoryId(5), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Not_found_item_returns_null()
    {
        Stub("/v0/item/5.json", 404, "");

        (await CreateClient().GetStoryAsync(new StoryId(5), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Deleted_item_returns_null()
    {
        Stub("/v0/item/5.json", 200, """{"id":5,"deleted":true,"type":"story","time":1570887781}""");

        (await CreateClient().GetStoryAsync(new StoryId(5), _ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Transient_server_error_is_retried()
    {
        _server.Given(Request.Create().WithPath("/v0/beststories.json").UsingGet())
            .InScenario("flaky").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));
        _server.Given(Request.Create().WithPath("/v0/beststories.json").UsingGet())
            .InScenario("flaky").WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("[1]"));

        var ids = await CreateClient().GetBestStoryIdsAsync(_ct);

        ids.ShouldBe([new StoryId(1)]);
        _server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Persistent_server_error_fails_after_retries()
    {
        Stub("/v0/beststories.json", 500, "");

        await Should.ThrowAsync<HttpRequestException>(CreateClient(maxRetries: 2).GetBestStoryIdsAsync(_ct));

        _server.LogEntries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Slow_upstream_times_out()
    {
        _server.Given(Request.Create().WithPath("/v0/beststories.json").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("[1]").WithDelay(TimeSpan.FromSeconds(3)));

        await Should.ThrowAsync<TimeoutRejectedException>(
            CreateClient(maxRetries: 1, attemptTimeout: TimeSpan.FromMilliseconds(200)).GetBestStoryIdsAsync(_ct));
    }

    public void Dispose() => _server.Dispose();

    private HackerNewsHttpClient CreateClient(int maxRetries = 3, TimeSpan? attemptTimeout = null)
    {
        var provider = TestServices.Build(options =>
        {
            options.BaseAddress = new Uri($"{_server.Url}/v0/");
            options.MaxRetryAttempts = maxRetries;
            // Generous by default so a slow WireMock under parallel test load is not mistaken for a timeout.
            options.AttemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout = TimeSpan.FromSeconds(60);
        });
        return provider.GetRequiredService<HackerNewsHttpClient>();
    }

    private void Stub(string path, int status, string body) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithBody(body));
}
