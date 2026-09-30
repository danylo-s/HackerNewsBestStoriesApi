using System.Globalization;

namespace HackerNews.Domain;

public readonly record struct StoryId
{
    public StoryId(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
