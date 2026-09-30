using System.Globalization;
using System.Threading.RateLimiting;
using HackerNews.Api.ErrorHandling;
using HackerNews.Api.Health;
using HackerNews.Api.Options;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HackerNews.Api;

internal static class ApiDependencyInjection
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ResponseCachingOptions>()
            .Bind(configuration.GetSection(ResponseCachingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi();

        services.AddHealthChecks()
            .AddCheck<CacheWarmupHealthCheck>("story-cache", tags: [ReadyTag]);

        services.AddOutputCache();
        services.AddOptions<OutputCacheOptions>()
            .Configure<IOptions<ResponseCachingOptions>>((output, caching) =>
                output.AddPolicy(ResponseCachingOptions.BestStoriesPolicy, policy => policy
                    .Expire(caching.Value.Duration)
                    .SetVaryByQuery("count")));

        services.AddRateLimiter(ConfigureRateLimiter);

        return services;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(RateLimitingOptions.PerIpPolicy, httpContext =>
        {
            var settings = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = settings.Window,
                QueueLimit = settings.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        });

        options.OnRejected = async (context, cancellationToken) =>
        {
            var httpContext = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                httpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails =
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "Rate limit exceeded. Retry after the period indicated by the Retry-After header.",
                },
            });
        };
    }
}
