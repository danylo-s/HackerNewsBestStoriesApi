using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using HackerNews.Application.Abstractions;
using HackerNews.Domain;

namespace HackerNews.Infrastructure.HackerNewsApi;

/// <summary>Typed HttpClient for the Hacker News Firebase API. Resilience is applied by the handler pipeline.</summary>
internal sealed class HackerNewsHttpClient(HttpClient httpClient) : IHackerNewsClient
{
    private static readonly Uri _bestStoriesUri = new("beststories.json", UriKind.Relative);

    public async Task<IReadOnlyList<StoryId>> GetBestStoryIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await httpClient.GetFromJsonAsync(_bestStoriesUri, HackerNewsJsonContext.Default.Int32Array, cancellationToken);
        return [.. (ids ?? []).Where(id => id > 0).Select(id => new StoryId(id))];
    }

    public async Task<Story?> GetStoryAsync(StoryId id, CancellationToken cancellationToken)
    {
        var uri = new Uri(string.Create(CultureInfo.InvariantCulture, $"item/{id.Value}.json"), UriKind.Relative);
        using var response = await httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync(HackerNewsJsonContext.Default.HnItem, cancellationToken);
        return HnItemMapper.ToStory(item);
    }
}
