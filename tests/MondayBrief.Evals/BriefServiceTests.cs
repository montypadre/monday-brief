using Microsoft.Extensions.Options;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Alerts;
using MondayBrief.Core.Briefs;
using MondayBrief.Core.Entities;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;
using Xunit;

namespace MondayBrief.Evals;

public sealed class BriefServiceTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private const string ValidBrief = """
        {
            "headline": "Revenue slipped 6.2% to $13,464.50 last week, and the candle keeps sliding.",
            "whats_up": ["Orders held up at 630, slightly ahead of 619 the week before."],
            "whats_down": ["Average order value fell to $21.37 from $23.20, so baskets are smaller."],
            "watch_list": ["The Magnolia Bay Soy Candle is down 48% over four weeks and online conversion sat at 1.61%."],
            "one_action": "It might be worth moving the candle to the front counter this week to see whether placement is the problem."
        }
        """;

    private BriefService Service(IAnthropicClient client)
    {
        var kpis = new KpiService(fixture.Db);
        return new BriefService(
            fixture.Db,
            kpis,
            new AlertService(fixture.Db, kpis),
            client,
            Options.Create(new AppOptions { AsOfDate = new DateOnly(2026, 8, 31) }),
            Options.Create(new AnthropicOptions { ApiKey = "test" }));
    }

    [Fact]
    public void Latest_week_is_the_moday_to_sunday_before_today()
    {
        Assert.Equal(new DateOnly(2026, 8, 24), Service(new FakeAnthropicClient()).LatestWeekStart());
    }

    [Fact]
    public async Task Fact_sheet_carries_the_weeks_real_numbers()
    {
        var facts = await Service(new FakeAnthropicClient()).BuildFactsAsync(new DateOnly(2026, 8, 24));

        Assert.Equal(new DateOnly(2026, 8, 30), facts.WeekEnd);
        Assert.Equal(new DateOnly(2026, 8, 17), facts.PreviousWeekStart);

        Assert.Equal(13_464.50m, facts.Revenue.Current);
        Assert.Equal(14_358.75m, facts.Revenue.Previous);
        Assert.Equal(-6.2, facts.Revenue.ChangePct);

        Assert.Equal(11_319.50m, facts.InStoreRevenue.Current);
        Assert.Equal(2_145.00m, facts.OnlineRevenue.Current);
        Assert.Equal(630, facts.Orders.Current);
        Assert.Equal(21.37m, facts.AverageOrderValue.Current);
        Assert.Equal(1.61m, facts.Conversion.Current);
        
        Assert.Equal("Bayside Logo Tee", facts.TopProducts[0].Name);
    }

    [Fact]
    public async Task Alerts_put_the_four_week_candle_decline_in_front_of_the_model()
    {
        var facts = await Service(new FakeAnthropicClient()).BuildFactsAsync(new DateOnly(2026, 8, 24));

        // Week over week the candle is too small to rank; the alert is how the brief can find it.
        Assert.Contains(facts.Alerts, a => a.Subject == "Magnolia Bay Soy Candle");
        Assert.Contains(facts.Alerts, a => a.Subject == "Online store");
    }

    [Fact]
    public async Task Generated_brief_is_stored_and_rendered()
    {
        var brief = await Service(new FakeAnthropicClient(FakeAnthropicClient.Text(ValidBrief)))
            .GeneratedAsync(new DateOnly(2026, 8, 24));

        Assert.Equal(new DateOnly(2026, 8, 24), brief.WeekStart);
        Assert.Contains("Magnolia Bay Soy Candle", brief.RenderedText);
        Assert.StartsWith("Revenue slipped", brief.RenderedText);

        var words = BriefService.CountWords(brief.RenderedText);
        Assert.InRange(words, 50, 170);
    }

    [Fact]
    public async Task Regenrating_repalces_rather_than_duplicates()
    {
        var service = Service(new FakeAnthropicClient(FakeAnthropicClient.Text(ValidBrief)));

        await service.GeneratedAsync(new DateOnly(2026, 8, 24));
        await service.GeneratedAsync(new DateOnly(2026, 8, 24));

        Assert.Single(fixture.Db.Briefs.Where(b => b.WeekStart == new DateOnly(2026, 8, 24)));
    }

    [Fact]
    public async Task Json_wrapped_in_code_fences_is_still_parsed()
    {
        var fenced = "```json\n" + ValidBrief + "\n```";

        var brief = await Service(new FakeAnthropicClient(FakeAnthropicClient.Text(fenced)))
            .GeneratedAsync(new DateOnly(2026, 8, 17));

        Assert.Contains("Revenue slipped", brief.RenderedText);
    }
}