using FluentValidation;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.BestStories;

internal sealed class GetBestStoriesQueryValidator : AbstractValidator<GetBestStoriesQuery>
{
    public GetBestStoriesQueryValidator(IOptions<BestStoriesOptions> options)
    {
        var maxCount = options.Value.MaxCount;

        RuleFor(q => q.Count)
            .InclusiveBetween(1, maxCount)
            .OverridePropertyName("count")
            .WithMessage($"'count' must be between 1 and {maxCount}.");
    }
}
