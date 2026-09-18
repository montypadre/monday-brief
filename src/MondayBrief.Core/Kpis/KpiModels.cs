namespace MondayBrief.Core.Kpis;

/// <summary>Raw totals for one window. Money stays in cents until it reaches a DTO.</summary>
public sealed record PeriodTotals(long RevenueCents, int Orders, int OnlineOrders, int Sessions)
{
    public decimal AverageOrderValueCents => Orders == 0 ? 0 : (decimal)RevenueCents / Orders;

    /// <summary>Conversion rate as percentage: Σ online orders ÷ Σ sessions.</summary>
    public double ConversionPct => Sessions == 0 ? 0 : 100.0 * OnlineOrders / Sessions;
}

public sealed record ProductTotals(string Sku, string Name, long RevenueCents, int Units);

public sealed record KpiCard(
    string Key,
    string Label,
    decimal Value,
    decimal PreviousValue,
    string Format,
    double? ChangePct,
    double? ChangePoints);

public sealed record TopProduct(string Sku, string Name, decimal Revenue, int Units);

public sealed record ProductDecline(string Sku, string Name, int Units, int PreviousUnits, double ChangePct);

public sealed record RangeInfo(DateOnly Start, DateOnly End, int Days, DateOnly PreviousStart, DateOnly PreviousEnd);

public sealed record KpiSummary(
    RangeInfo Range,
    IReadOnlyList<KpiCard> Cards,
    IReadOnlyList<TopProduct> TopProducts,
    IReadOnlyList<ProductDecline> Decliners);