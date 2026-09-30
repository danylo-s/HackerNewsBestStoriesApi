using System.Net;

namespace HackerNews.Api.IntegrationTests;

public sealed class PlatformEndpointTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Liveness_is_healthy_without_touching_hacker_news()
    {
        await using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.HackerNews.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Readiness_is_unhealthy_until_the_cache_is_warm()
    {
        await using var host = new ApiTestHost(new Dictionary<string, string?> { ["HackerNews:RefreshEnabled"] = "true" });
        host.Stub("/v0/beststories.json", 500, "");
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Readiness_becomes_healthy_once_the_background_refresh_warms_the_cache()
    {
        await using var host = new ApiTestHost(new Dictionary<string, string?> { ["HackerNews:RefreshEnabled"] = "true" });
        host.StubBestStories(1);
        host.StubStory(1, score: 5);
        using var client = host.CreateClient();

        // Generous deadline: under parallel test load the first upstream call can be slow.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var status = HttpStatusCode.ServiceUnavailable;
        while (status != HttpStatusCode.OK && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, _ct);
            using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), _ct);
            status = response.StatusCode;
        }

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_document_is_served_in_development()
    {
        await using var host = new ApiTestHost();
        using var client = host.CreateClient();

        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), _ct);

        document.ShouldContain("/api/v1/stories/best");
    }
}
