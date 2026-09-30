using System.ComponentModel.DataAnnotations;

namespace HackerNews.Api.Options;

/// <summary>Server-side output cache duration; also advertised to clients via Cache-Control max-age.</summary>
public sealed class ResponseCachingOptions
{
    public const string SectionName = "ResponseCaching";
    public const string BestStoriesPolicy = "best-stories";

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(30);
}
