using HackerNews.Application.BestStories;
using Microsoft.Extensions.Options;

namespace HackerNews.Application.Tests;

public sealed class GetBestStoriesQueryValidatorTests
{
    private readonly GetBestStoriesQueryValidator _validator = new(Options.Create(new BestStoriesOptions { MaxCount = 200 }));

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(200)]
    public void Count_within_range_is_valid(int count)
    {
        _validator.Validate(new GetBestStoriesQuery(count)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void Count_outside_range_is_invalid_and_reported_as_count(int count)
    {
        var result = _validator.Validate(new GetBestStoriesQuery(count));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("count");
    }

    [Fact]
    public void Max_count_is_configurable()
    {
        var validator = new GetBestStoriesQueryValidator(Options.Create(new BestStoriesOptions { MaxCount = 10 }));

        validator.Validate(new GetBestStoriesQuery(10)).IsValid.ShouldBeTrue();
        validator.Validate(new GetBestStoriesQuery(11)).IsValid.ShouldBeFalse();
    }
}
