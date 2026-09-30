namespace HackerNews.Application.Abstractions;

/// <summary>Hacker News could not be reached and no cached data was available to fall back on.</summary>
public sealed class HackerNewsUnavailableException : Exception
{
    public HackerNewsUnavailableException()
        : base("Hacker News is unavailable and no cached data exists.")
    {
    }

    public HackerNewsUnavailableException(string message)
        : base(message)
    {
    }

    public HackerNewsUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
