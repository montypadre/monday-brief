using System.Globalization;
using System.Text;
using MondayBrief.Core.Ai;
using MondayBrief.Core.Briefs;

namespace MondayBrief.Evals.Live;

public sealed record EvalOutcome(string Id, string Kind, string Question, bool Passed, string Detail);

public sealed class EvalRunner(AskService ask, BusinessTools tools, BriefService briefs)
{
    private static readonly string[] RefusalPhrases =
    [
        "don't have", "do not have", "not available", "no cost", "isn't available",
        "is not available", "can't", "cannot", "unable to",
    ];

    public async Task<List<EvalOutcome>> RunAsync(CancellationToken cancellationToken = default)
    {
        var outcomes = new List<EvalOutcome>();

        foreach (var testCase in EvalCases.All)
        {
            outcomes.Add(await RunCaseAsync(testCase, cancellationToken));
        }

        outcomes.Add(await RunBriefCaseAsync(cancellationToken));
        return outcomes;
    }

    private async Task<EvalOutcome> RunCaseAsync(EvalCase testCase, CancellationToken cancellationToken)
    {
        var result = await ask.AskAsync(testCase.Question, cancellationToken);
        var answer = result.Answer;
        var toolNumbers = result.Sources.SelectMany(s => NumberCheck.FromPayload(s.Payload)).ToList();
        var unsupported = NumberCheck.Unsupported(answer, toolNumbers);
        var problems = new List<string>();

        if (unsupported.Count > 0)
        {
            problems.Add($"figures not in any tool result: {string.Join(", ", unsupported)}");
        }

        if (testCase.Kind == EvalKind.Refusal)
        {
            var normalized = Normalize(answer);
            if (!RefusalPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add("did not decline");
            }
        }
        else
        {
            var expected = await testCase.ExpectedValue(tools);
            if (expected is { } value && !ContainsValue(answer, value))
            {
                problems.Add($"expected {value} in the answer");
            }

            if (testCase.MustMention is { } mention && !answer.Contains(mention, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"did not mention '{mention}'");
            }
        }

        return new EvalOutcome(
            testCase.Id,
            testCase.Kind.ToString(),
            testCase.Question,
            problems.Count == 0,
            problems.Count == 0 ? Shorten(answer) : $"{string.Join("; ", problems)} - answer: {Shorten(answer)}");
    }

    private async Task<EvalOutcome> RunBriefCaseAsync(CancellationToken cancellationToken)
    {
        var weekStart = briefs.LatestWeekStart();
        var facts = await briefs.BuildFactsAsync(weekStart, cancellationToken);
        var brief = await briefs.GeneratedAsync(weekStart, cancellationToken);

        var factNumbers = NumberCheck.FromPayload(
            System.Text.Json.JsonSerializer.Serialize(facts));
        var unsupported = NumberCheck.Unsupported(brief.RenderedText, factNumbers);
        var words = BriefService.CountWords(brief.RenderedText);
        var problems = new List<string>();

        if (!brief.RenderedText.Contains("candle", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("did not mention the declining candle");
        }

        if (words is < BriefService.MinWords or > BriefService.MaxWords)
        {
            problems.Add($"{words} words, outside {BriefService.MinWords}-{BriefService.MaxWords}");
        }

        if (unsupported.Count > 0)
        {
            problems.Add($"figures not in the fact sheet: {string.Join(", ", unsupported)}");
        }

        return new EvalOutcome(
            "b01", "Brief", "Weekly brief for " + weekStart.ToString("yyyy-MM-dd"),
            problems.Count == 0,
            problems.Count == 0 ? $"{words} words, all figures grounded" : string.Join("; ", problems));
    }

    /// <summary>The answer may write 15560.5 as "$15,560.50", so compare numerically.</summary>
    private static bool ContainsValue(string answer, decimal expected) => 
        NumberCheck.Unsupported(expected.ToString(CultureInfo.InvariantCulture), NumberCheck.Extract(answer)).Count == 0;

    private static string Shorten(string text) =>
        text.Length <= 90 ? text: text[..90].Replace("\n", " ") + "...";

    private static string Normalize(string text) =>
        text.Replace('\u2019', '\'').Replace('\u2018', '\'');

    public static string Report(IReadOnlyList<EvalOutcome> outcomes, string model)
    {
        var passed = outcomes.Count(o => o.Passed);
        var report = new StringBuilder();

        report.AppendLine("# Eval report");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Model `{model}` · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"**{passed}/{outcomes.Count} passed.**");
        report.AppendLine();
        report.AppendLine("Checks: every figure in an answer must appear in a tool result; known-answer questions must");
        report.AppendLine("quote the value computed from the database; questions the data cannot answer must be declined.");
        report.AppendLine("the weekly brief must find the declining product and stay within its word limit.");
        report.AppendLine();
        report.AppendLine("| ID | Type | Question | Pass | Detail |");
        report.AppendLine("|---|---|---|:---:|---|");

        foreach (var outcome in outcomes)
        {
            var detail = outcome.Detail.Replace("|", "\\|");
            report.AppendLine(CultureInfo.InvariantCulture,
                $"| {outcome.Id} | {outcome.Kind} | {outcome.Question} | {(outcome.Passed ? "✅" : "❌")} | {detail} |");
        }

        return report.ToString();
    }
}