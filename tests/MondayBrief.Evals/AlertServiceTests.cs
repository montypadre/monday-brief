using MondayBrief.Core.Alerts;
using MondayBrief.Core.Kpis;
using Xunit;

namespace MondayBrief.Evals;

public sealed class AlertServiceTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private static readonly DateOnly AsOf = new(2026, 8, 31);

    private AlertService Alerts => new(fixture.Db, new KpiService(fixture.Db));

    [Fact]
    public async Task Two_alerts_fire_as_of_the_demo_date()
    {
        var triggered = await Alerts.EvaluateAsOfAsync(AsOf);

        Assert.Equal("conversion-below-2pct|product-units-drop", string.Join("|", triggered.Select(a => a.RuleKey)));
    }

    [Fact]
    public async Task Conversion_alert_reports_the_seven_day_rate()
    {
        var alert = (await Alerts.EvaluateAsOfAsync(AsOf)).Single(a => a.RuleKey == "conversion-below-2pct");

        Assert.Equal(1.61m, alert.Value);
        Assert.Equal(2.0m, alert.Threshold);
        Assert.Equal(new DateOnly(2026, 8, 24), alert.WindowStart);
        Assert.Equal(new DateOnly(2026, 8, 30), alert.WindowEnd);
        Assert.Equal("Online store", alert.Subject);
    }

    [Fact]
    public async Task Product_alert_finds_the_candle_and_nothing_else()
    {
        var product = (await Alerts.EvaluateAsOfAsync(AsOf)).Where(a => a.RuleKey == "product-units-drop").ToList();

        var candle = Assert.Single(product);
        Assert.Equal("Magnolia Bay Soy Candle", candle.Subject);
        Assert.Equal(-48.0m, candle.Value);
        Assert.Equal(new DateOnly(2026, 8, 3), candle.WindowStart);
        Assert.Equal(new DateOnly(2026, 7, 6), candle.ComparedToStart);
    }

    [Fact]
    public async Task Weekly_revenue_is_within_threshold_as_of_the_demo_date()
    {
        // Down 6.2% - real, but nowhere near the 20% rule. The headline hides the candle.
        Assert.DoesNotContain(await Alerts.EvaluateAsOfAsync(AsOf), a => a.RuleKey == "weekly-revenue-drop");
    }

    [Fact]
    public async Task Storm_week_trips_the_revenue_rule()
    {
        var stormWeek = new DateRange(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16));

        var alert = (await Alerts.EvaluateWindowAsync(stormWeek)).Single(a => a.RuleKey == "weekly-revenue-drop");

        Assert.Equal(-33.9m, alert.Value);
        Assert.Equal("Total revenue", alert.Subject);
        Assert.Equal(new DateOnly(2026, 8, 3), alert.ComparedToStart);
    }
}