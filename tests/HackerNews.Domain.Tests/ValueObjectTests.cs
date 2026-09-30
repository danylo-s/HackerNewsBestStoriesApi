namespace HackerNews.Domain.Tests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void StoryId_must_be_positive(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new StoryId(value));
    }

    [Fact]
    public void StoryId_has_value_equality()
    {
        new StoryId(42).ShouldBe(new StoryId(42));
    }

    [Fact]
    public void Score_cannot_be_negative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Score(-1));
    }

    [Fact]
    public void Score_is_comparable_by_value()
    {
        new Score(10).CompareTo(new Score(5)).ShouldBePositive();
        new Score(5).CompareTo(new Score(10)).ShouldBeNegative();
        new Score(7).CompareTo(new Score(7)).ShouldBe(0);
    }
}
