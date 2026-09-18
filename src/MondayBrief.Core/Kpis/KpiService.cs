using System.Data.SqlTypes;

using Microsoft.EntityFrameworkCore;
using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Kpis;

/// <summary>
/// Every number the dashboard, the brief and the AI tools report comes from here, so a metric is
/// defined exactly once. Rounding happens here too: the AI can only be checked against tool results
/// if both quote identically rounded figures.
/// </summary>
public sealed class KpiService(MondayBriefDbContext db)
{
    /// <summary>Products need this many units in the baseline window before they can be ranked as decliners.</summary>
    public const int MinUnitsForRanking = 60;

    public async Task<KpiSummary> GetSummaryAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var previous = range.Previous();

        var current = await GetTotalsAsync(range, cancellationToken);
        var before = await GetTotalsAsync(previous, cancellationToken);

        var currentProducts = await GetProductTotalsAsync(range, cancellationToken);
        var previousProducts = await GetProductTotalsAsync(previous, cancellationToken);

        return new KpiSummary(
            new RangeInfo(range.Start, range.End, range.Days, previous.Start, previous.End),
            BuildCards(current, before),
            BuildTopProducts(currentProducts, 5),
            BuildDecliners(currentProducts, previousProducts, 3));
    }

    public async Task<PeriodTotals> GetTotalsAsync(DateRange range, CancellationToken cancellationToken = default)
    {
        var orders = db.Orders.Where(o => o.BusinessDate >= range.Start && o.BusinessDate <= range.End);

        var revenue = await orders.SumAsync(o => (long?)o.SubtotalCents, cancellationToken) ?? 0;
        var count = await orders.CountAsync(cancellationToken);
        var online = await orders.CountAsync(o => o.ChannelId == ChannelIds.Online, cancellationToken);

        var sessions = await db.DailyTraffic
            .Where(t => t.Date >= range.Start && t.Date <= range.End)
            .SumAsync(t => (int?)t.Sessions, cancellationToken) ?? 0;

        return new PeriodTotals(revenue, count, online, sessions);
    }

    public async Task<IReadOnlyList<ProductTotals>> GetProductTotalsAsync(
        DateRange range, CancellationToken cancellationToken = default) =>
        await db.OrderLines
            .Where(l => l.Order.BusinessDate >= range.Start && l.Order.BusinessDate <= range.End)
            .GroupBy(l => new { l.Product.Sku, l.Product.Name })
            .Select(g => new ProductTotals(
                g.Key.Sku,
                g.Key.Name,
                g.Sum(l => l.LineTotalCents),
                g.Sum(l => l.Quantity)))
            .ToListAsync(cancellationToken);

    private static List<KpiCard> BuildCards(PeriodTotals current, PeriodTotals previous) =>
    [
        Money("revenue", "Revenue", current.RevenueCents, previous.RevenueCents),
        Count("orders", "Orders", current.Orders, previous.Orders),
        Money("aov", "Average order value", current.AverageOrderValueCents, previous.AverageOrderValueCents),
        Rate("conversion", "Online conversion", current.ConversionPct, previous.ConversionPct),
    ];

    private static KpiCard Money(string key, string label, decimal currentCents, decimal previousCents) =>
        new (key, label,
             Math.Round(currentCents / 100m, 2, MidpointRounding.AwayFromZero),
             Math.Round(previousCents / 100m, 2, MidpointRounding.AwayFromZero),
             "currency",
             PercentChange((double)currentCents, (double)previousCents),
             null);
    
    private static KpiCard Count(string key, string label, int current, int previous) =>
        new(key, label, current, previous, "number", PercentChange(current, previous), null);

    private static KpiCard Rate(string key, string label, double currentPct, double previousPct) =>
        new(key, label,
            (decimal)Math.Round(currentPct, 2, MidpointRounding.AwayFromZero),
            (decimal)Math.Round(previousPct, 2, MidpointRounding.AwayFromZero),
            "percent",
            PercentChange(currentPct, previousPct),
            Math.Round(currentPct - previousPct, 2, MidpointRounding.AwayFromZero));

    /// <summary>Relative change in percent, or null when there is no baseline to compare against.</summary>
    private static double? PercentChange(double current, double previous) =>
        previous == 0 ? null : Math.Round(100.0 * (current - previous) / previous, 1, MidpointRounding.AwayFromZero);

    private static List<TopProduct> BuildTopProducts(IReadOnlyList<ProductTotals> products, int take) => 
        products 
            .OrderByDescending(p => p.RevenueCents)
            .ThenBy(p => p.Sku, StringComparer.Ordinal)
            .Take(take)
            .Select(p => new TopProduct(
                p.Sku,
                p.Name,
                Math.Round(p.RevenueCents / 100m, 2, MidpointRounding.AwayFromZero),
                p.Units))
            .ToList();

    /// <summary>
    /// Ranks by unit change, not revenue, and ignores anything below the baseline floor: without it,
    /// a low-volume product's normal noise outranks a real decline.
    /// </summary>
    private static List<ProductDecline> BuildDecliners(
        IReadOnlyList<ProductTotals> current, IReadOnlyList<ProductTotals> previous, int take)
    {
        var currentUnits = current.ToDictionary(p => p.Sku, StringComparer.Ordinal);

        return previous
            .Where(p => p.Units >= MinUnitsForRanking)
            .Select(p =>
            {
                var units = currentUnits.TryGetValue(p.Sku, out var now) ? now.Units : 0;
                return new ProductDecline(
                    p.Sku,
                    p.Name,
                    units,
                    p.Units,
                    Math.Round(100.0 * (units - p.Units) / p.Units, 1, MidpointRounding.AwayFromZero));
            })
            .Where(d => d.ChangePct < 0)
            .OrderBy(d => d.ChangePct)
            .ThenBy(d => d.Sku, StringComparer.Ordinal)
            .Take(take)
            .ToList();
    }
}