using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using HackerNews.Api.Options;
using HackerNews.Application.BestStories;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace HackerNews.Api.Endpoints;

internal static class BestStoriesEndpoints
{
    public static IEndpointRouteBuilder MapBestStoriesEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1/stories")
            .WithTags("Stories")
            .RequireRateLimiting(RateLimitingOptions.PerIpPolicy);

        v1.MapGet("/best", GetBestStoriesAsync)
            .WithName("GetBestStories")
            .WithSummary("Best Hacker News stories")
            .WithDescription("Returns the best `count` stories from Hacker News, ordered by score descending.")
            .CacheOutput(ResponseCachingOptions.BestStoriesPolicy)
            .Produces(StatusCodes.Status304NotModified)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<StoryDto>>, ValidationProblem, StatusCodeHttpResult>> GetBestStoriesAsync(
        [Description("Number of stories to return, between 1 and the configured maximum (default 200).")] int? count,
        ISender sender,
        HttpContext httpContext,
        IOptions<ResponseCachingOptions> cachingOptions,
        CancellationToken cancellationToken)
    {
        if (count is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["count"] = ["'count' is required."] });
        }

        var stories = await sender.Send(new GetBestStoriesQuery(count.Value), cancellationToken);

        var etag = ComputeETag(stories);
        var responseHeaders = httpContext.Response.GetTypedHeaders();
        responseHeaders.ETag = etag;
        responseHeaders.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = cachingOptions.Value.Duration };

        var ifNoneMatch = httpContext.Request.GetTypedHeaders().IfNoneMatch;
        if (ifNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(etag, useStrongComparison: false)))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(stories);
    }

    private static EntityTagHeaderValue ComputeETag(IReadOnlyList<StoryDto> stories)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(stories, JsonSerializerOptions.Web);
        var hash = Convert.ToHexString(SHA256.HashData(payload).AsSpan(0, 16));
        return new EntityTagHeaderValue($"\"{hash}\"");
    }
}
