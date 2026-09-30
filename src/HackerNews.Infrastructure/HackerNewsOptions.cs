using System.ComponentModel.DataAnnotations;

namespace HackerNews.Infrastructure;

public sealed class HackerNewsOptions : IValidatableObject
{
    public const string SectionName = "HackerNews";

    private const string MinDuration = "00:00:00.001";
    private const string MaxDuration = "1.00:00:00";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://hacker-news.firebaseio.com/v0/");

    /// <summary>How long the best-stories ID list is cached.</summary>
    [Range(typeof(TimeSpan), MinDuration, MaxDuration)]
    public TimeSpan BestStoriesTtl { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How long an individual item is cached.</summary>
    [Range(typeof(TimeSpan), MinDuration, MaxDuration)]
    public TimeSpan ItemTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a last-known-good copy is kept to serve when Hacker News is unavailable.</summary>
    [Range(typeof(TimeSpan), MinDuration, "7.00:00:00")]
    public TimeSpan StaleTtl { get; set; } = TimeSpan.FromHours(24);

    public bool RefreshEnabled { get; set; } = true;

    /// <summary>Background refresh period; must be shorter than <see cref="BestStoriesTtl"/>.</summary>
    [Range(typeof(TimeSpan), MinDuration, MaxDuration)]
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Age at which the background refresh refetches an item; must be shorter than <see cref="ItemTtl"/>.</summary>
    [Range(typeof(TimeSpan), MinDuration, MaxDuration)]
    public TimeSpan ItemRefreshInterval { get; set; } = TimeSpan.FromMinutes(4);

    [Range(typeof(TimeSpan), MinDuration, "00:05:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), MinDuration, "00:10:00")]
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    [Range(typeof(TimeSpan), MinDuration, "00:01:00")]
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RefreshInterval >= BestStoriesTtl)
        {
            yield return new ValidationResult(
                $"{nameof(RefreshInterval)} must be shorter than {nameof(BestStoriesTtl)} so the ID list never expires.",
                [nameof(RefreshInterval)]);
        }

        if (ItemRefreshInterval >= ItemTtl)
        {
            yield return new ValidationResult(
                $"{nameof(ItemRefreshInterval)} must be shorter than {nameof(ItemTtl)} so items never expire.",
                [nameof(ItemRefreshInterval)]);
        }

        if (TotalRequestTimeout <= AttemptTimeout)
        {
            yield return new ValidationResult(
                $"{nameof(TotalRequestTimeout)} must be longer than {nameof(AttemptTimeout)}.",
                [nameof(TotalRequestTimeout)]);
        }
    }
}
