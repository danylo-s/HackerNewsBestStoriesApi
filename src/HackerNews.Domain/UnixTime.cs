namespace HackerNews.Domain;

/// <summary>Converts Hacker News Unix timestamps (seconds) to UTC <see cref="DateTimeOffset"/>.</summary>
public static class UnixTime
{
    public static DateTimeOffset ToDateTimeOffset(long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
