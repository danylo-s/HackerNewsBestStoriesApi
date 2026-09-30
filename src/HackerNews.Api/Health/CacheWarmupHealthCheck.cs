using System.Globalization;
using HackerNews.Infrastructure;
using HackerNews.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HackerNews.Api.Health;

/// <summary>
/// Readiness based on the background refresh rather than a live call to Hacker News,
/// so probes never add upstream load.
/// </summary>
internal sealed class CacheWarmupHealthCheck(
    CacheWarmupState state,
    IOptions<HackerNewsOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    private const int MissedRefreshesBeforeDegraded = 3;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.RefreshEnabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Background refresh disabled; stories are fetched on demand."));
        }

        if (state.LastSuccessfulRefresh is not { } lastRefresh)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Story cache has not been warmed yet."));
        }

        var age = timeProvider.GetUtcNow() - lastRefresh;
        var description = string.Create(CultureInfo.InvariantCulture, $"Last successful refresh {age.TotalSeconds:0}s ago.");

        return Task.FromResult(age <= settings.RefreshInterval * MissedRefreshesBeforeDegraded
            ? HealthCheckResult.Healthy(description)
            : HealthCheckResult.Degraded(description + " Serving cached data."));
    }
}
