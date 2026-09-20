using MondayBrief.Core.Ai;

namespace MondayBrief.Evals.Live;

public enum EvalKind
{
    /// <summary>The answer must contain a specific figure computed from the database.</summary>
    KnownAnswer,

    /// <summary>The data cannot answer this; the model must say so and quote no figures.</summary>
    Refusal,
}

public sealed record EvalCase(
    string Id,
    EvalKind Kind,
    string Question,
    Func<BusinessTools, Task<decimal?>> ExpectedValue,
    string? MustMention = null);

/// <summary>
/// 25 questions with answers derived from the database at run time, plus 5 the data cannot answer.
/// Nothing here is hardcoded: reseed the data and the answer key moves with it.
/// </summary>
public static class EvalCases
{
    private static readonly DateOnly AsOf = new(2026, 8, 31);

    public static IReadOnlyList<EvalCase> All =>
    [
        Metric("m01", "What was total revenue in March 2026?", "revenue", "2026-03-01", "2026-03-31"),
        Metric("m02", "What was online revenue in March 2026", "revenue", "2026-03-01", "2026-03-31", "online"),
        Metric("m03", "What was in-store revenue in March 2026?", "revenue", "2026-03-01", "2026-03-31", "instore"),
        Metric("m04", "How many orders were there in June 2026?", "orders", "2026-06-01", "2026-06-30"),
        Metric("m05", "What was the average order value in June 2026?", "aov", "2026-06-01", "2026-06-30"),
        Metric("m06", "What as the online conversion rate in June 2026?", "conversion", "2026-06-01", "2026-06-30"),
        Metric("m07", "How many website sessions were there in July 2026?", "sessions", "2026-07-01", "2026-07-31"),
        Metric("m08", "What was total revenue in December 2025?", "revenue", "2025-12-01", "2025-12-31"),
        Metric("m09", "What was revenue for the week of 24 August 2026 through 30 August 2026?", "revenue", "2026-08-24", "2026-08-30"),
        Metric("m10", "What was online revenue in February 2026?", "revenue", "2025-02-01", "2026-02-28", "online"),
        Metric("m11", "What was the conversion rate from 1 August 2026 to 30 August 2026?", "conversion", "2026-08-01", "2026-08-30"),
        Metric("m12", "How many orders did the online store take in May 2026?", "orders", "2026-05-01", "2026-05-31", "online"),
        Metric("m13", "What was in-store revenue on 12 August 2026?", "revenue", "2026-08-12", "2026-08-12", "instore"),
        Metric("m14", "What was the average order value in December 2025?", "aov", "2025-12-01", "2025-12-31"),
        Metric("m15", "What was total revenue for the last 30 days?", "revenue", "2026-08-01", "2026-08-30"),

        Compare("c01", "How did revenue in August 2026 compare with June 2026?", "revenue",
            "2026-06-01", "2026-06-30", "2026-08-01", "2026-08-30"),
        Compare("c02", "Did orders go up or down from July 2026 to August 2026?", "orders",
            "2026-07-01", "2026-07-31", "2026-08-01", "2026-08-30"),
        Compare("c03", "How did online conversion in August 2026 compare with June 2026?", "conversion",
            "2026-06-01", "2026-06-30", "2026-08-01", "2026-08-30"),
        Compare("c04", "Compare December 2025 revenue with November 2025.", "revenue",
            "2025-11-01", "2025-11-30", "2025-12-01", "2025-12-31"),
        Compare("c05", "How did in-store revenue in February 2026 compare with January 2026?", "revenue",
            "2026-01-01", "2026-01-31", "2026-02-01", "2026-02-28", "instore"),

        TopProduct("p01", "What was the best selling product by revenue in June through August 2026?", "2026-06-01", "2026-08-30", "tee"),
        TopProduct("p02", "Which product made the most revenue in December 2025?", "2025-12-01", "2025-12-31", "hoodie"),
        Declining("p03", "Which product is declining the most from June through August 2026?", "2026-06-01", "2026-08-30"),
        Declining("p04", "Which product fell the most between 3 August 2026 and 30 August 2026?", "2026-08-03", "2026-08-30"),

        new EvalCase("a01", EvalKind.KnownAnswer, "Are there any alerts I should know about?",
            _ => Task.FromResult<decimal?>(null), MustMention: "candle"),

        Refuse("r01", "What is my profit margin?"),
        Refuse("r02", "What was my margin on the Eastern Shore Hoodie last month?"),
        Refuse("r03", "How much did I spend on inventory in July 2026?"),
        Refuse("r04", "What was revenue in October 2026?"),
        Refuse("r05", "How many staff hours did the coffee counter use last week?"),
    ];

    private static EvalCase Metric(string id, string question, string metric, string start, string end, string? channel = null) =>
        new(id, EvalKind.KnownAnswer, question, async tools =>
        {
            var result = await tools.GetMetricAsync(metric, DateOnly.Parse(start), DateOnly.Parse(end), channel);
            return result.Data is MetricValue value ? value.Value : null;
        });

    private static EvalCase Compare(
        string id, string question, string metric, string aStart, string aEnd, string bStart, string bEnd, string? channel = null) =>
        new(id, EvalKind.KnownAnswer, question, async tools =>
        {
            var result = await tools.ComparePeriodsAsync(
                metric, DateOnly.Parse(aStart), DateOnly.Parse(aEnd), DateOnly.Parse(bStart), DateOnly.Parse(bEnd), channel);
            return result.Data is PeriodComparison comparison ? comparison.PeriodB.Value : null;
        });

    private static EvalCase TopProduct(string id, string question, string start, string end, string mustMention) =>
        new(id, EvalKind.KnownAnswer, question, async tools =>
        {
            var result = await tools.TopProductsAsync(1, DateOnly.Parse(start), DateOnly.Parse(end));
            return result.Data is ProductRanking ranking ? ranking.Products[0].Revenue : null;
        }, MustMention: mustMention);

    private static EvalCase Declining(string id, string question, string start, string end) =>
        new(id, EvalKind.KnownAnswer, question, async tools =>
        {
            var result = await tools.TopProductsAsync(1, DateOnly.Parse(start), DateOnly.Parse(end), "declining");
            return result.Data is ProductRanking ranking ? (decimal)ranking.Products[0].ChangePct! : null;
        }, MustMention: "candle");

    private static EvalCase Refuse(string id, string question) =>
        new (id, EvalKind.Refusal, question, _ => Task.FromResult<decimal?>(null));
}