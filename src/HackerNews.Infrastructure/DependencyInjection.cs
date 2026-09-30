using HackerNews.Application.Abstractions;
using HackerNews.Infrastructure.Caching;
using HackerNews.Infrastructure.HackerNewsApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace HackerNews.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HackerNewsOptions>()
            .Bind(configuration.GetSection(HackerNewsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHybridCache();
        services.AddSingleton<StoryCache>();
        services.AddSingleton<CacheWarmupState>();

        services.AddHttpClient<HackerNewsHttpClient>((sp, client) =>
            {
                client.BaseAddress = sp.GetRequiredService<IOptions<HackerNewsOptions>>().Value.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HackerNews.BestStories/1.0");
            })
            .AddStandardResilienceHandler()
            .Configure((resilience, sp) => ConfigureResilience(resilience, sp.GetRequiredService<IOptions<HackerNewsOptions>>().Value));

        // User requests: cache first, Hacker News only on a miss.
        services.AddScoped<IHackerNewsClient>(sp =>
            new CachingHackerNewsClient(sp.GetRequiredService<HackerNewsHttpClient>(), sp.GetRequiredService<StoryCache>()));

        // Background refresh: always talks to Hacker News and writes into the cache.
        services.AddScoped<IStoryCacheWarmer>(sp => ActivatorUtilities.CreateInstance<StoryCacheWarmer>(
            sp, (IHackerNewsClient)sp.GetRequiredService<HackerNewsHttpClient>()));
        services.AddHostedService<BestStoriesRefreshService>();

        return services;
    }

    private static void ConfigureResilience(HttpStandardResilienceOptions resilience, HackerNewsOptions options)
    {
        resilience.AttemptTimeout.Timeout = options.AttemptTimeout;
        resilience.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;
        resilience.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
        resilience.Retry.Delay = options.RetryBaseDelay;
        resilience.Retry.UseJitter = true;

        // The circuit breaker sampling window must be at least twice the attempt timeout.
        var minimumSampling = options.AttemptTimeout * 2;
        if (resilience.CircuitBreaker.SamplingDuration < minimumSampling)
        {
            resilience.CircuitBreaker.SamplingDuration = minimumSampling;
        }
    }
}
