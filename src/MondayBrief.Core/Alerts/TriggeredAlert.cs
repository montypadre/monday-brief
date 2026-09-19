namespace MondayBrief.Core.Alerts;

/// <summary>One rule firing, with the numbers that made it fire.</summary>
public sealed record TriggeredAlert(
    string RuleKey,
    string RuleName,
    string Subject,
    string Metric,
    decimal Value,
    decimal Threshold,
    string Unit,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    DateOnly? ComparedToStart,
    DateOnly? ComparedToEnd,
    string message);