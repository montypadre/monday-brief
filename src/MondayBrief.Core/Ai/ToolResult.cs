namespace MondayBrief.Core.Ai;

/// <summary>
/// What one tool call produced. Either Data (serialized back to the model) or Error (a message the
/// model relays to the user). Bad input is never an exception: the model has to be able to say
/// "I don't have that data".
/// </summary>
public sealed record ToolResult(string Tool, object? Data, string? Error)
{
    public bool IsSuccess => Error is null;

    public static ToolResult Success(string tool, object data) => new(tool, data, null);

    public static ToolResult Failure(string tool, string error) => new(tool, null, error);
}

public sealed record MetricValue(
    string Metric,
    string Channel,
    DateOnly Start,
    DateOnly End,
    decimal Value,
    string Unit);

public sealed record PeriodValue(DateOnly Start, DateOnly End, decimal Value);

public sealed record PeriodComparison(
    string Metric,
    string Channel,
    PeriodValue PeriodA,
    PeriodValue PeriodB,
    decimal Change,
    double? ChangePct,
    string Unit);

public sealed record ProductRow(string Sku, string Name, decimal Revenue, int Units, int? PreviousUnits, double? ChangePct);

public sealed record ProductRanking(
    string Direction,
    DateOnly Start,
    DateOnly End,
    DateOnly? ComparedToStart,
    DateOnly? ComparedToEnd,
    int MinimumUnits,
    IReadOnlyList<ProductRow> Products);

public sealed record AlertList(DateOnly? Start, DateOnly? End, IReadOnlyList<Alerts.TriggeredAlert> Alerts);