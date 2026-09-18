namespace MondayBrief.Core.Ingestion;

public sealed record SourceResult(
    string SourceSystem,
    string DisplayName,
    int ProductsRead,
    int OrdersRead,
    int TrafficRowsRead);

/// <summary>What one ingestion run did. Returned rather than logged, so Core stays free of logging.</summary>
public sealed record IngestionReport(
    IReadOnlyList<SourceResult> Sources,
    int ProductsInserted,
    int ProductsUpdated,
    int OrdersInserted,
    int OrdersSkipped,
    int OrderLinesInserted,
    int TrafficRowsInserted,
    int TrafficRowsUpdated,
    TimeSpan Elapsed)
{
    public static IngestionReport Skipped() => 
        new([], 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);

    public override string ToString() =>
        $"{OrdersInserted:N0} orders ({OrdersSkipped:N0} already present), {OrderLinesInserted:N0} lines, " +
        $"{ProductsInserted:N0}+{ProductsUpdated:N0} products, {TrafficRowsInserted:N0} traffic days " +
        $"in {Elapsed.TotalSeconds:0.0}s";
}