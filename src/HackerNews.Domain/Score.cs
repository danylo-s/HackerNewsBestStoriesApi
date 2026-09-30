namespace HackerNews.Domain;

public readonly record struct Score : IComparable<Score>
{
    public Score(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    public int Value { get; }

    public int CompareTo(Score other) => Value.CompareTo(other.Value);

    public static bool operator <(Score left, Score right) => left.CompareTo(right) < 0;

    public static bool operator >(Score left, Score right) => left.CompareTo(right) > 0;

    public static bool operator <=(Score left, Score right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Score left, Score right) => left.CompareTo(right) >= 0;
}
