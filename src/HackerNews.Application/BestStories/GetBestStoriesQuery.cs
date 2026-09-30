using Mediator;

namespace HackerNews.Application.BestStories;

public sealed record GetBestStoriesQuery(int Count) : IQuery<IReadOnlyList<StoryDto>>;
