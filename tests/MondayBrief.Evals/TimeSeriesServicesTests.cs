using MondayBrief.Core.Kpis;
using Xunit;

namespace MondayBrief.Evals;

public sealed class TimeSeriesServiceTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private static readonly DateOnly AsOf = new(2026, 8, 31);

    private TimeSeriesService Series => new(fixture.Db);

    private static TimeSeriesRequest Request(string metric, string? by = null, string bucket = "day", string range = "30d")
    {
        Assert.True(TimeSeriesRequest.TryParse(metric, by, bucket, range, AsOf, out var request, out var error), error);
        return request;
    }

    [Fact]
    public async Task Daily_revenue_splits_into_one_series_per_channel()
    {
        var result = await Series.GetAsync(Request("revenue", by: "channel"));

        Assert.Equal("InStore|Online", string.Join("|", result.Series.Select(s => s.Key)));
        Assert.Equal("currency", result.Unit);
        Assert.All(result.Series, s => Assert.Equal(30, s.Points.Count));
    }

    [Fact]
    public async Task Storm_closure_appears_as_zero_in_store_with_online_still_selling()
    {
        var result = await Series.GetAsync(Request("revenue", by: "channel"));
        var stormDay = new DateOnly(2026, 8, 12);

        var inStore = result.Series.Single(s => s.Key == "InStore").Points.Single(p => p.Date == stormDay);
        var online = result.Series.Single(s => s.Key == "Online").Points.Single(p => p.Date == stormDay);

        Assert.Equal(0m, inStore.Value);
        Assert.Equal(159.00m, online.Value);
    }

    [Fact]
    public async Task Weekly_buckets_start_on_monday_and_show_the_storm_week_dip()
    {
        var result = await Series.GetAsync(Request("revenue", bucket: "week", range: "28d"));
        var points = result.Series.Single().Points;

        Assert.Equal(4, points.Count);
        Assert.All(points, p => Assert.Equal(DayOfWeek.Monday, p.Date.DayOfWeek));
        Assert.Equal(new DateOnly(2026, 8, 3), points[0].Date);

        Assert.Equal(13_634.75m, points[0].Value);
        Assert.Equal(9_011.00m, points[1].Value);   // storm week
        Assert.Equal(14_358.75m, points[2].Value);
        Assert.Equal(13_464.50m, points[3].Value);
    }

    [Fact]
    public async Task Conversion_is_orders_over_sessions_within_each_bucket()
    {
        var daily = await Series.GetAsync(Request("conversion"));
        Assert.Equal("percent", daily.Unit);
        Assert.Equal(1.51m, daily.Series.Single().Points.Single(p => p.Date == new DateOnly(2026, 8, 30)).Value);

        var weekly = await Series.GetAsync(Request("conversion", bucket: "week", range: "28d"));
        Assert.Equal(1.52m, weekly.Series.Single().Points[0].Value);
        Assert.Equal(1.40m, weekly.Series.Single().Points[1].Value);
    }

    [Fact]
    public async Task Weekly_aov_is_computed_per_bucket()
    {
        var result = await Series.GetAsync(Request("aov", bucket: "week", range: "28d"));

        Assert.Equal(21.64m, result.Series.Single().Points[0].Value);
        Assert.Equal(21.51m, result.Series.Single().Points[1].Value);
    }

    [Theory]
    [InlineData("sessions", "channel")]
    [InlineData("conversion", "channel")]
    [InlineData("profit", null)]
    [InlineData("revenue", "category")]
    public void Unavailable_combinations_are_rejected(string metric, string? by)
    {
        Assert.False(TimeSeriesRequest.TryParse(metric, by, "day", "30d", AsOf, out _, out var error));
        Assert.NotNull(error);
    }
}