using MondayBrief.Core.Kpis;
using Xunit;

namespace MondayBrief.Evals;

public sealed class DateRangeTests
{
    private static readonly DateOnly AsOf = new (2026, 8, 31);

    [Fact]
    public void Last_30_days_ends_on_the_last_complete_day()
    {
        var range = DateRange.LastDays(AsOf, 30);

        Assert.Equal(new DateOnly(2026, 8, 1), range.Start);
        Assert.Equal(new DateOnly(2026, 8, 30), range.End);
        Assert.Equal(30, range.Days);
    }

    [Fact]
    public void Previous_window_is_the_same_length_and_immediately_before()
    {
        var previous = DateRange.LastDays(AsOf, 30).Previous();

        Assert.Equal(new DateOnly(2026, 7, 2), previous.Start);
        Assert.Equal(new DateOnly(2026, 7, 31), previous.End);
        Assert.Equal(30, previous.Days);
    }

    [Fact]
    public void Last_week_is_the_monday_to_sunday_before_today()
    {
        var range = DateRange.LastDays(AsOf, 7);

        Assert.Equal(new DateOnly(2026, 8, 24), range.Start);
        Assert.Equal(new DateOnly(2026, 8, 30), range.End);
        Assert.Equal(DayOfWeek.Monday, range.Start.DayOfWeek);
    }

    [Theory]
    [InlineData("30d", 30)]
    [InlineData("7D", 7)]
    [InlineData(null, 30)]
    public void Valid_ranges_parse(string? input, int expectedDays)
    {
        Assert.True(RangeParser.TryParse(input, AsOf, out var range, out _));
        Assert.Equal(expectedDays, range.Days);
    }

    [Theory]
    [InlineData("last month")]
    [InlineData("0d")]
    [InlineData("400d")]
    public void Invalid_ranges_are_rejected_with_a_message(string input)
    {
        Assert.False(RangeParser.TryParse(input, AsOf, out _, out var error));
        Assert.NotNull(error);
    }
}