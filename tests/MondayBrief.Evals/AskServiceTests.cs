using Microsoft.Extensions.Options;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Alerts;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;
using Xunit;

namespace MondayBrief.Evals;

public sealed class AskServiceTests(IngestedDatabaseFixture fixture) : IClassFixture<IngestedDatabaseFixture>
{
    private AskService Service(FakeAnthropicClient client)
    {
        var kpis = new KpiService(fixture.Db);
        var tools = new BusinessTools(kpis, new AlertService(fixture.Db, kpis));
        return new AskService(
            client,
            tools,
            kpis,
            Options.Create(new AppOptions { AsOfDate = new DateOnly(2026, 8, 31) }),
            Options.Create(new AnthropicOptions { ApiKey = "test", MaxToolRounds = 4 }));
    }

    [Fact]
    public async Task A_tool_call_is_executed_and_recorded_as_a_source()
    {
        var client = new FakeAnthropicClient(
            FakeAnthropicClient.Calls(BusinessTools.GetMetric, new { metric = "revenue", start = "2026-03-01", end = "2026-03-31", channel = "online" }),
            FakeAnthropicClient.Text("Online revenue in March was $15,560.50."));

        var result = await Service(client).AskAsync("What was online revenue in March?");

        Assert.Equal("Online revenue in March was $15,560.50.", result.Answer);
        Assert.Equal(2, result.Rounds);

        var source = Assert.Single(result.Sources);
        Assert.Equal(BusinessTools.GetMetric, source.Tool);
        Assert.True(source.Ok);
        Assert.Contains("metric=revenue", source.Arguments);
    }

    [Fact]
    public async Task The_tool_result_sent_back_contains_the_real_number()
    {
        var client = new FakeAnthropicClient(
            FakeAnthropicClient.Calls(BusinessTools.GetMetric, new { metric = "revenue", start = "2026-03-01", end = "2026-03-31", channel = "online"}),
            FakeAnthropicClient.Text("done"));

        await Service(client).AskAsync("What was online revenue in March?");

        // Second request carries the tool_result the model will quote from.
        var secondRequest = client.Requests[1].Messages.ToJsonString();
        Assert.Contains("15560.5", secondRequest);
    }

    [Fact]
    public async Task A_refusal_tool_call_is_marked_and_the_reason_goes_back_to_the_model()
    {
        var client = new FakeAnthropicClient(
            FakeAnthropicClient.Calls(BusinessTools.GetMetric, new { metric = "margin", start = "2026-03-01", end = "2026-03-31" }),
            FakeAnthropicClient.Text("I don't have cost data, so I can't work out margin."));

        var result = await Service(client).AskAsync("What's my profit margin?");

        var source = Assert.Single(result.Sources);
        Assert.False(source.Ok);
        Assert.Contains("no cost or profit data", source.Error);
        Assert.Contains("no cost or profit data", client.Requests[1].Messages.ToJsonString());
    }

    [Fact]
    public async Task The_system_prompt_states_today_and_the_data_window()
    {
        var client = new FakeAnthropicClient(FakeAnthropicClient.Text("hello"));

        await Service(client).AskAsync("hello");

        var prompt = client.Requests[0].System;
        Assert.Contains("2026-08-31", prompt);
        Assert.Contains("2025-09-01 to 2026-08-30", prompt);
        Assert.Contains("Every number in your answer must come from a tool result", prompt);
    }

    [Fact]
    public async Task A_model_that_never_stops_calling_tools_is_cut_off()
    {
        var client = new FakeAnthropicClient(
            FakeAnthropicClient.Calls(BusinessTools.GetMetric, new { metric = "revenue", start = "2026-03-01", end = "2026-03-31" }));

        var result = await Service(client).AskAsync("loop forever");

        Assert.Equal(4, result.Rounds);
        Assert.Contains("wasn't able to finish", result.Answer);
    }
}