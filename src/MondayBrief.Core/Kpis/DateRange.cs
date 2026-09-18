namespace MondayBrief.Core.Kpis;

/// <summary>An inclusive span of business dates.</summary>
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>The equal-length window immediately before this one.</summary>
    public DateRange Previous() => new(Start.AddDays(-Days), Start.AddDays(-1));

    /// <summary>
    /// The last <paramref name="days"/> complete days of <paramref name="asOf"/>.
    /// asOf is "today", so the range ends the day before it.
    /// </summary>
    public static DateRange LastDays(DateOnly asOf, int days)
    {
        var end = asOf.AddDays(-1);
        return new DateRange(end.AddDays(-(days - 1)), end);
    }

    public override string ToString() => $"{Start:yyyy-MM-dd}..{End:yyyy-MM-dd}";
}

public static class RangeParser
{
    public const int MaxDays = 365;

    /// <summary>Parses a dashboard range such as "7d", "30d" or "90d".</summary>
    public static bool TryParse(string? value, DateOnly asOf, out DateRange range, out string? error)
    {
        range = default;
        error = null;

        var text = (value ?? "30d").Trim().ToLowerInvariant();
        if (!text.EndsWith('d') || !int.TryParse(text[..^1], out var days))
        {
            error = $"Range '{value}' is not understood. Use a day count such as 7d, 30d, or 90d.";
            return false;
        }

        if (days < 1 || days > MaxDays)
        {
            error = $"Range must be between 1d and {MaxDays}d.";
            return false;
        }

        range = DateRange.LastDays(asOf, days);
        return true;
    }
}