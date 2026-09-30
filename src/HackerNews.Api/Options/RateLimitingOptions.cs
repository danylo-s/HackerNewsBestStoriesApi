using System.ComponentModel.DataAnnotations;

namespace HackerNews.Api.Options;

/// <summary>Inbound fixed-window rate limit, partitioned per client IP.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string PerIpPolicy = "per-ip";

    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    [Range(0, 1_000)]
    public int QueueLimit { get; set; }
}
