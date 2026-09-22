using MondayBrief.Core.Ingestion;
using Xunit;

namespace MondayBrief.Evals;

public sealed class BusinessClockTests
{
    private readonly BusinessClock _clock = BusinessClock.FromId("America/Chicago");

    [Fact]
    public void Late_evening_utc_order_belongs_to_the_previous_local_day()
    {
        // 04:30 UTC on Aug 12 is 11:30 pm CDT on Aug 11.
        var utc = new DateTime(2026, 8, 12, 4, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 8, 11), _clock.BusinessDateOf(utc));
    }

    [Fact]
    public void Winter_offset_is_six_hours()
    {
        // CST (UTC-6) in December: 7:00 am local is 13:00 UTC.
        var local = new DateTime(2025, 12, 13, 7, 0, 0);
        Assert.Equal(new DateTime(2025, 12, 13, 13, 0, 0, DateTimeKind.Utc), _clock.LocalToUtc(local));
    }

    [Fact]
    public void Summer_offset_is_five_hours()
    {
        // CDT (UTC-5) in August: 7:00 am local is 12:00 UTC.
        var local = new DateTime(2026, 8, 11, 7, 0, 0);
        Assert.Equal(new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc), _clock.LocalToUtc(local));
    }

    [Fact]
    public void Non_utc_input_is_rejected()
    {
        var local = new DateTime(2026, 8, 11, 23, 0, 0, DateTimeKind.Local);
        Assert.Throws<ArgumentException>(() => _clock.BusinessDateOf(local));
    }
}