namespace HackerNews.Domain.Tests;

public sealed class UnixTimeTests
{
    [Fact]
    public void ToDateTimeOffset_converts_seconds_to_utc_offset()
    {
        var result = UnixTime.ToDateTimeOffset(1570887781);

        result.ShouldBe(new DateTimeOffset(2019, 10, 12, 13, 43, 1, TimeSpan.Zero));
        result.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ToDateTimeOffset_of_zero_is_unix_epoch()
    {
        UnixTime.ToDateTimeOffset(0).ShouldBe(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void ToDateTimeOffset_rejects_negative_seconds()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => UnixTime.ToDateTimeOffset(-1));
    }
}
