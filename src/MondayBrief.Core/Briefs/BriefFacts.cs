namespace MondayBrief.Core.Briefs;

public sealed record FactValue(decimal Current, decimal Previous, double? ChangePct, string Unit);

public sealed record FactProduct(string Name, decimal Revenue, int Units, int? Previous, double? ChangePct);

public sealed record FactAlert(string Subject, string Message);

/// <summary>
/// Everything the brief is allowed to mention. The model receives this and nothing else, so any figure 
/// in the finished brief that is not in here was invented.
/// </summary>
public sealed record BriefFacts(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    DateOnly PreviousWeekStart,
    DateOnly PreviousWeekEnd,
    FactValue Revenue,
    FactValue InStoreRevenue,
    FactValue OnlineRevenue,
    FactValue Orders,
    FactValue AverageOrderValue,
    FactValue Conversion,
    IReadOnlyList<FactProduct> TopProducts,
    IReadOnlyList<FactProduct> Decliners,
    IReadOnlyList<FactAlert> Alerts);

/// <summary>The model's structured output.</summary>
public sealed record BriefContent(
    string Headline,
    IReadOnlyList<string> WhatsUp,
    IReadOnlyList<string> WhatsDown,
    IReadOnlyList<string> WatchList,
    string OneAction);