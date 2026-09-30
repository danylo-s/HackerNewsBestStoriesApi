using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HackerNews.Api.IntegrationTests;

public sealed class BestStoriesEndpointTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_best_stories_ordered_by_score_in_the_documented_shape()
    {
        await using var host = new ApiTestHost();
        host.StubBestStories(1, 2, 3);
        host.StubStory(1, score: 100);
        host.StubStory(2, score: 300);
        host.StubStory(3, score: 200);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stories/best?count=2", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct));
        var stories = json.RootElement.EnumerateArray().ToList();
        stories.Count.ShouldBe(2);

        var first = stories[0];
        first.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["title", "uri", "postedBy", "time", "score", "commentCount"]);
        first.GetProperty("title").GetString().ShouldBe("Story 2");
        first.GetProperty("uri").GetString().ShouldBe("https://example.com");
        first.GetProperty("postedBy").GetString().ShouldBe("user2");
        first.GetProperty("time").GetString().ShouldBe("2019-10-12T13:43:01+00:00");
        first.GetProperty("score").GetInt32().ShouldBe(300);
        first.GetProperty("commentCount").GetInt32().ShouldBe(20);
        stories[1].GetProperty("score").GetInt32().ShouldBe(200);
    }

    [Fact]
    public async Task Skips_deleted_and_non_story_items_and_links_text_posts_to_discussion()
    {
        await using var host = new ApiTestHost();
        host.StubBestStories(1, 2, 3, 4);
        host.StubStory(1, score: 10, url: null);
        host.StubStory(2, score: 50, deleted: true);
        host.StubStory(3, score: 40, type: "job");
        host.Stub("/v0/item/4.json", 200, "null");
        using var client = host.CreateClient();

        var body = await client.GetStringAsync(new Uri("/api/v1/stories/best?count=10", UriKind.Relative), _ct);

        using var json = JsonDocument.Parse(body);
        var story = json.RootElement.EnumerateArray().ShouldHaveSingleItem();
        story.GetProperty("uri").GetString().ShouldBe("https://news.ycombinator.com/item?id=1");
    }

    [Theory]
    [InlineData("count=0")]
    [InlineData("count=201")]
    [InlineData("count=-3")]
    public async Task Out_of_range_count_returns_validation_problem(string query)
    {
        await using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/v1/stories/best?{query}", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct));
        json.RootElement.GetProperty("errors").GetProperty("count").GetArrayLength().ShouldBe(1);
        host.HackerNews.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_count_returns_validation_problem()
    {
        await using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stories/best", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct));
        json.RootElement.GetProperty("errors").GetProperty("count").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Non_numeric_count_returns_problem_details()
    {
        await using var host = new ApiTestHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stories/best?count=abc", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Max_count_is_configurable()
    {
        await using var host = new ApiTestHost(new Dictionary<string, string?> { ["BestStories:MaxCount"] = "5" });
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stories/best?count=6", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Exceeding_the_rate_limit_returns_429_with_retry_after()
    {
        await using var host = new ApiTestHost(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "2" });
        host.StubBestStories(1);
        host.StubStory(1, score: 1);
        using var client = host.CreateClient();
        var uri = new Uri("/api/v1/stories/best?count=1", UriKind.Relative);

        (await client.GetAsync(uri, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(uri, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var limited = await client.GetAsync(uri, _ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
        limited.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Responses_carry_cache_headers_and_honour_if_none_match()
    {
        await using var host = new ApiTestHost();
        host.StubBestStories(1);
        host.StubStory(1, score: 1);
        using var client = host.CreateClient();
        var uri = new Uri("/api/v1/stories/best?count=1", UriKind.Relative);

        using var first = await client.GetAsync(uri, _ct);
        first.Headers.CacheControl!.Public.ShouldBeTrue();
        first.Headers.CacheControl.MaxAge.ShouldNotBeNull();
        var etag = first.Headers.ETag.ShouldNotBeNull();

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag));
        using var second = await client.SendAsync(request, _ct);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Repeated_requests_do_not_hit_hacker_news_again()
    {
        await using var host = new ApiTestHost();
        host.StubBestStories(1, 2);
        host.StubStory(1, score: 1);
        host.StubStory(2, score: 2);
        using var client = host.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.GetStringAsync(new Uri($"/api/v1/stories/best?count={(i % 2) + 1}", UriKind.Relative), _ct);
        }

        host.HackerNews.LogEntries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Hacker_news_outage_without_cache_returns_503_problem()
    {
        await using var host = new ApiTestHost();
        host.Stub("/v0/beststories.json", 500, "");
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/stories/best?count=1", UriKind.Relative), _ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
