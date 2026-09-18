using MondayBrief.Core.Kpis;
using Xunit;

namespace MondayBrief.Evals;

public sealed class KpiServiceTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private static readonly DateOnly AsOf = new(2026, 8, 31);
    private static readonly DateRange Last30 = DateRange.LastDays(AsOf, 30);

    private KpiService Kpis => new(fixture.Db);

    [Fact]
    public async Task Last_30_days_totals_match_the_ingested_data()
    {
        var totals = await Kpis.GetTotalsAsync(Last30);

        Assert.Equal(5_585_125L, totals.RevenueCents);
        Assert.Equal(2_537, totals.Orders);
        Assert.Equal(188, totals.OnlineOrders);
        Assert.Equal(12_043, totals.Sessions);
        Assert.Equal(1.56, Math.Round(totals.ConversionPct, 2));
    }

    [Fact]
    public async Task Cards_carry_period_over_period_deltas()
    {
        var summary = await Kpis.GetSummaryAsync(Last30);

        var revenue = summary.Cards.Single(c => c.Key == "revenue");
        Assert.Equal(55_851.25m, revenue.Value);
        Assert.Equal(57_877.75m, revenue.PreviousValue);
        Assert.Equal(-3.5, revenue.ChangePct);

        var aov = summary.Cards.Single(c => c.Key == "aov");
        Assert.Equal(22.01m, aov.Value);
    }

    [Fact]
    public async Task Conversion_reports_both_relative_change_and_percentage_points()
    {
        var conversion = (await Kpis.GetSummaryAsync(Last30)).Cards.Single(c => c.Key == "conversion");

        Assert.Equal(1.56m, conversion.Value);
        Assert.Equal(1.82m, conversion.PreviousValue);
        Assert.Equal(-14.3, conversion.ChangePct);
        Assert.Equal(-0.26, conversion.ChangePoints);
    }

    [Fact]
    public async Task Top_products_are_ranked_by_revenue()
    {
        var summary = await Kpis.GetSummaryAsync(Last30);

        Assert.Equal("AP-TEE-01", summary.TopProducts[0].Sku);
        Assert.Equal(5_304.00m, summary.TopProducts[0].Revenue);
        Assert.Equal(5, summary.TopProducts.Count);
    }

    [Fact]
    public async Task The_planted_decline_is_the_top_decliner_by_a_wide_margin()
    {
        var decliners = (await Kpis.GetSummaryAsync(Last30)).Decliners;

        Assert.Equal("LG-CNDL-01", decliners[0].Sku);
        Assert.Equal(109, decliners[0].PreviousUnits);
        Assert.Equal(57, decliners[0].Units);
        Assert.Equal(-47.7, decliners[0].ChangePct);

        // The next-worse product is nowhere near it, which is what makes this findable.
        Assert.True(decliners[1].ChangePct > -15, $"runner-up was {decliners[1].Sku} at {decliners[1].ChangePct}%");
    }
}