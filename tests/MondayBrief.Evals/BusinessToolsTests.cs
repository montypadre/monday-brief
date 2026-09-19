using MondayBrief.Core.Ai;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Alerts;
using Xunit;

using System.Reflection.Metadata;

namespace MondayBrief.Evals;

public sealed class BusinessToolsTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private BusinessTools Tools => new(new KpiService(fixture.Db), new AlertService(fixture.Db, new KpiService(fixture.Db)));

    private static T Data<T>(ToolResult result)
    {
        Assert.True(result.IsSuccess, result.Error);
        return Assert.IsType<T>(result.Data);
    }

    [Fact]
    public async Task Get_metric_returns_online_revenue_for_a_month()
    {
        var value = Data<MetricValue>(
            await Tools.GetMetricAsync("revenue", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "online"));

        Assert.Equal(15_560.50m, value.Value);
        Assert.Equal("online", value.Channel);
        Assert.Equal("currency", value.Unit);
        Assert.Equal(new DateOnly(2026, 3, 1), value.Start);
    }

    [Fact]
    public async Task Get_metric_returns_conversion_for_a_month()
    {
        var value = Data<MetricValue>(
            await Tools.GetMetricAsync("conversion", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));

        Assert.Equal(2.34m, value.Value);
        Assert.Equal("percent", value.Unit);
    }

    [Fact]
    public async Task Compare_periods_does_the_arithmetic_so_the_model_does_not()
    {
        var comparison = Data<PeriodComparison>(await Tools.ComparePeriodsAsync(
            "revenue",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 30)));

        Assert.Equal(63_117.00m, comparison.PeriodA.Value);
        Assert.Equal(55_851.25m, comparison.PeriodB.Value);
        Assert.Equal(-7_265.75m, comparison.Change);
        Assert.Equal(-11.5, comparison.ChangePct);
    }

    [Fact]
    public async Task Top_products_ranks_by_revenue()
    {
        var ranking = Data<ProductRanking>(
            await Tools.TopProductsAsync(3, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 30)));

        Assert.Equal("AP-TEE-01", ranking.Products[0].Sku);
        Assert.Equal(16_614.00m, ranking.Products[0].Revenue);
        Assert.Equal(3, ranking.Products.Count);
    }

    [Fact]
    public async Task Declining_products_find_the_planted_problem()
    {
        var ranking = Data<ProductRanking>(
            await Tools.TopProductsAsync(3, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 30), "declining"));

        // Compared against the equal-length window immediately before.
        Assert.Equal(new DateOnly(2026, 3, 2), ranking.ComparedToStart);
        Assert.Equal(new DateOnly(2026, 5, 31), ranking.ComparedToEnd);

        var worst = ranking.Products[0];
        Assert.Equal("LG-CNDL-01", worst.Sku);
        Assert.Equal(463, worst.PreviousUnits);
        Assert.Equal(311, worst.Units);
        Assert.Equal(-32.8, worst.ChangePct);
    }

    [Fact]
    public async Task Margin_questions_have_no_metric_to_call()
    {
        var result = await Tools.GetMetricAsync("margin", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        Assert.False(result.IsSuccess);
        Assert.Contains("no cost or profit data", result.Error);
    }

    [Fact]
    public async Task Dates_outside_the_data_window_are_refused()
    {
        var result = await Tools.GetMetricAsync("revenue", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.False(result.IsSuccess);
        Assert.Contains("2026-08-30", result.Error);
    }

    [Fact]
    public async Task Conversion_cannot_be_filtered_by_channel()
    {
        var result = await Tools.GetMetricAsync("conversion", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "instore");

        Assert.False(result.IsSuccess);
        Assert.Contains("web analytics", result.Error);
    }

    [Fact]
    public async Task List_alerts_returns_the_two_current_alerts()
    {
        var list = Data<AlertList>(await Tools.ListAlertsAsync(new DateOnly(2026, 8, 31)));

        Assert.Equal(2, list.Alerts.Count);
        Assert.Contains(list.Alerts, a => a.Subject == "Magnolia Bay Soy Candle");
    }

    [Fact]
    public async Task List_alerts_rejects_a_half_specified_window()
    {
        var result = await Tools.ListAlertsAsync(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 1));

        Assert.False(result.IsSuccess);
        Assert.Contains("both start and end", result.Error);
    }
}