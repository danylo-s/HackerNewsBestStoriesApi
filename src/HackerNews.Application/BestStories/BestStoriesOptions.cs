using System.ComponentModel.DataAnnotations;

namespace HackerNews.Application.BestStories;

public sealed class BestStoriesOptions
{
    public const string SectionName = "BestStories";

    /// <summary>Upper bound for <c>count</c>; beststories.json returns up to ~200 IDs.</summary>
    [Range(1, 500)]
    public int MaxCount { get; set; } = 200;

    /// <summary>Maximum number of concurrent item lookups per request.</summary>
    [Range(1, 64)]
    public int MaxConcurrency { get; set; } = 8;
}
