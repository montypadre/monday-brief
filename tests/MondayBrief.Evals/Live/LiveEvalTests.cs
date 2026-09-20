using Microsoft.Extensions.Options;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Alerts;
using MondayBrief.Core.Briefs;
using MondayBrief.Core.Kpis;
using MondayBrief.Core.Options;
using Xunit;
using Xunit.Abstractions;

namespace MondayBrief.Evals.Live;

/// <summary>
/// Runs the whole question set against the real model. Skipped unless RUN_EVALS=1, because it costs
/// money and needs a network; ordinary `dotnet test` stays free.
/// </summary>
public sealed class LiveEvalTests(IngestedDatabaseFixture fixture, ITestOutputHelper output) : IClassFixture<IngestedDatabaseFixture>
{
    private const int PassMark = 22;

    [Fact]
    public async Task Eval_suite_passes_the_mark()
    {
        if (Environment.GetEnvironmentVariable("RUN_EVALS") != "1")
        {
            output.WriteLine("Skipped. Set RUN_EVALS=1 and ANTHROPIC_API_KEY to run.");
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
        var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5-20251001";

        var ai = Options.Create(new AnthropicOptions { ApiKey = apiKey, Model = model });
        var app = Options.Create(new AppOptions { AsOfDate = new DateOnly(2026, 8, 31)});

        using var http = new HttpClient();
        var client = new AnthropicClient(http, ai);

        var kpis = new KpiService(fixture.Db);
        var alerts = new AlertService(fixture.Db, kpis);
        var tools = new BusinessTools(kpis, alerts);
        var ask = new AskService(client, tools, kpis, app, ai);
        var briefs = new BriefService(fixture.Db, kpis, alerts, client, app, ai);

        var outcomes = await new EvalRunner(ask, tools, briefs).RunAsync();
        var report = EvalRunner.Report(outcomes, model);

        var root = RepoPaths.Root ?? Directory.GetCurrentDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "eval-report.md"), report);

        output.WriteLine(report);

        var passed = outcomes.Count(o => o.Passed);
        Assert.True(passed >= PassMark, $"{passed}/{outcomes.Count} passed; see eval-report.md");
    }
}