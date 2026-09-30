using System.Collections.Concurrent;
using HackerNews.Application.Abstractions;
using HackerNews.Domain;
using Mediator;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.BestStories;

internal sealed class GetBestStoriesQueryHandler(IHackerNewsClient client, IOptions<BestStoriesOptions> options)
    : IQueryHandler<GetBestStoriesQuery, IReadOnlyList<StoryDto>>
{
    public async ValueTask<IReadOnlyList<StoryDto>> Handle(GetBestStoriesQuery query, CancellationToken cancellationToken)
    {
        var ids = await client.GetBestStoryIdsAsync(cancellationToken);
        var stories = new ConcurrentBag<Story>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.Value.MaxConcurrency,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(ids, parallelOptions, async (id, ct) =>
        {
            if (await client.GetStoryAsync(id, ct) is { } story)
            {
                stories.Add(story);
            }
        });

        return
        [
            .. stories
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Id.Value)
                .Take(query.Count)
                .Select(StoryDto.From),
        ];
    }
}
