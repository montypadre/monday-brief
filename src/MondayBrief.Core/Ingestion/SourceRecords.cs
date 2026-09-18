namespace MondayBrief.Core.Ingestion;

/// <summary>A product as described by a source system, keyed by the canonical POS SKU.</summary>
public sealed record SourceProduct(string Sku, string Name, string Category, long ListPriceCents);

public sealed record SourceOrderLine(string Sku, string Name, int Quantity, long UnitPriceCents, long LineTotalCents);

/// <summary>An order normalized to canonical terms: UTC timestamp, business date, cents, SKUs.</summary>
public sealed record SourceOrder(
    string SourceSystem,
    string ExternalId,
    int ChannelId,
    DateTime PlacedUtc,
    DateOnly BusinessDate,
    IReadOnlyList<SourceOrderLine> Lines)
{
    public long SubtotalCents => Lines.Sum(l => l.LineTotalCents);
}

public sealed record SourceTraffic(DateOnly Date, int Sessions, int Users, int PageViews);

/// <summary>Everything one adpater read from its source.</summary>
public sealed record SourceBatch(
    string SourceSystem,
    IReadOnlyList<SourceProduct> Products,
    IReadOnlyList<SourceOrder> Orders,
    IReadOnlyList<SourceTraffic> Traffic)
{
    public static SourceBatch Empty(string sourceSystem) => new(sourceSystem, [], [], []);
}