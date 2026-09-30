using Microsoft.AspNetCore.Mvc.Testing;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HackerNews.Api.IntegrationTests;

/// <summary>An isolated API instance (own caches and rate limiter) talking to its own fake Hacker News.</summary>
internal sealed class ApiTestHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTestHost(IReadOnlyDictionary<string, string?>? settings = null)
    {
        HackerNews = WireMockServer.Start();
        var defaults = new Dictionary<string, string?>
        {
            ["HackerNews:BaseAddress"] = $"{HackerNews.Url}/v0/",
            ["HackerNews:RefreshEnabled"] = "false",
            ["HackerNews:MaxRetryAttempts"] = "1",
            ["HackerNews:AttemptTimeout"] = "00:00:10",
            ["HackerNews:TotalRequestTimeout"] = "00:01:00",
            ["HackerNews:RetryBaseDelay"] = "00:00:00.010",
            ["RateLimiting:PermitLimit"] = "1000",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            defaults[key] = value;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in defaults)
            {
                builder.UseSetting(key, value);
            }
        });
    }

    public WireMockServer HackerNews { get; }

    public HttpClient CreateClient() => _factory.CreateClient();

    public void StubBestStories(params int[] ids) =>
        Stub("/v0/beststories.json", 200, $"[{string.Join(',', ids)}]");

    public void StubStory(int id, int score, string? url = "https://example.com", string type = "story", bool deleted = false)
    {
        var urlJson = url is null ? string.Empty : $"\"url\":\"{url}\",";
        Stub(
            $"/v0/item/{id}.json",
            200,
            $$"""{"id":{{id}},"type":"{{type}}","by":"user{{id}}","title":"Story {{id}}",{{urlJson}}"score":{{score}},"time":1570887781,"descendants":{{id * 10}},"deleted":{{(deleted ? "true" : "false")}}}""");
    }

    public void Stub(string path, int status, string body) =>
        HackerNews.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithBody(body));

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        HackerNews.Dispose();
    }
}
