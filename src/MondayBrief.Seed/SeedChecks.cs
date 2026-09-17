using System.Globalization;
using System.Text;

using static MondayBrief.Seed.PlantedEvents;

namespace MondayBrief.Seed;

public sealed record CheckResult(string Event, string Check, string Detail, bool Passed);


/// <summary>
/// Verifies the planted events are actually findable in the generated data. Runs on the simulation,
/// not the database; the eval suite does its own ground-truth math against the DB on Friday.
/// </summary>
public sealed class SeedChecks
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Products need at least this many units in the baseline period to be ranked as risers or decliners.
    /// top_products(direction) must use the same floor, or low-volume noise outranks real trends.
    /// </summary>
    public const int MinUnitsForRanking = 60;

    private readonly Dictionary<(string Sku, DateOnly Date), int> _units = new();
    private readonly Dictionary<(SimChannel Channel, DateOnly Date), long> _revenue = new();
    private readonly Dictionary<DateOnly, int> _onlineOrders = new();
    private readonly Dictionary<DateOnly, int> _sessions = new();
    private readonly Simulation _sim;

    public SeedChecks(Simulation sim)
    {
        _sim = sim;
        foreach (var o in sim.Orders)
        {
            Add(_revenue, (o.Channel, o.BusinessDate), o.SubtotalCents);
            if (o.Channel == SimChannel.Online)
            {
                _onlineOrders[o.BusinessDate] = _onlineOrders.GetValueOrDefault(o.BusinessDate) + 1;
            }

            foreach (var l in o.Lines)
            {
                _units[(l.Product.Sku, o.BusinessDate)] = _units.GetValueOrDefault((l.Product.Sku, o.BusinessDate)) + l.Quantity;
            }
        }

        foreach (var t in sim.Traffic)
        {
            _sessions[t.Date] = t.Sessions;
        }
    }

    public IReadOnlyList<CheckResult> Run()
    {
        var r = new List<CheckResult>();

        // 1. Mardi Gras
        var mgPeak = AvgDaily(InStore, MardiGrasPeakStart, FatTuesday);
        var mgBase = AvgDaily(InStore, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 29));
        var mgLift = mgPeak / mgBase - 1;
        r.Add(new("Mardi Gras", "Final 10 days in-store revenue vs March baseline is up ≥45%", $"{Pct(mgLift)}", mgLift >= 0.45));
        var ash = InStore(AshWednesday, AshWednesday);
        var fat = InStore(FatTuesday, FatTuesday);
        r.Add(new("Mardi Gras", "Ash Wednesday in-store revenue < 60% of Fat Tuesday", $"{Money(ash)} vs {Money(fat)}", ash < fat * 0.6));

        // 2. Weather closure
        var closedInStore = InStore(StormClosedStart, StormClosedEnd);
        r.Add(new("Weather closure", "In-store revenue is $0 on Aug 12–13", Money(closedInStore), closedInStore == 0));
        var closedOnline = OnlineOrders(StormClosedStart, StormClosedEnd);
        r.Add(new("Weather closure", "Online orders continue on Aug 12–13", $"{closedOnline} orders", closedOnline > 0));
        var stormSessions = Sessions(StormClosedStart, StormClosedEnd) / 2.0;
        var priorSessions = Sessions(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9)) / 7.0;
        r.Add(new("Weather closure", "Sessions dip only slightly (≥75% of prior week's daily avg)",
            $"{stormSessions:0} vs {priorSessions:0}/day", stormSessions >= priorSessions * 0.75));
        var stormWeek = Total(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16));
        var weekBefore = Total(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9));
        var stormWow = stormWeek / (double)weekBefore - 1;
        r.Add(new("Weather closure", "Week of Aug 10 total revenue down >20% WoW (fires revenue alert)", Pct(stormWow), stormWow < -0.20));

        // 3. Holidays
        var peak = AvgDaily(Total, HolidayPeakStart, ChristmasEve.AddDays(-1));
        var october = AvgDaily(Total, new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31));
        r.Add(new("Holiday ramp", "Dec 13–23 daily revenue ≥1.6× October", $"{peak / october:0.00}×", peak >= october * 1.6));
        var bf = InStore(BlackFriday, BlackFriday);
        var earlyNov = AvgDaily(InStore, new DateOnly(2025, 11, 1), new DateOnly(2025, 11, 14));
        r.Add(new("Holiday ramp", "Black Friday in-store ≥2× early-November daily avg", $"{bf / earlyNov:0.00}×", bf >= earlyNov * 2));
        var closedHolidays = InStore(Thanksgiving, Thanksgiving) + InStore(Christmas, Christmas);
        r.Add(new("Holiday ramp", "Store closed Thanksgiving and Christmas", Money(closedHolidays), closedHolidays == 0));
        var afterXmas = AvgDaily(Total, Christmas.AddDays(1), new DateOnly(2025, 12, 31));
        r.Add(new("Holiday ramp", "Dec 26–31 daily revenue < 60% of peak", $"{afterXmas / peak:0.00}×", afterXmas < peak * 0.6));

        // 4. Declining product
        AddDeclineCheck(r, "June vs August units",
            new(2026, 6, 1), new(2026, 6, 30), new(2026, 8, 1), new(2026, 8, 30), minBaseline: MinUnitsForRanking, maxChange: -0.40);
        AddDeclineCheck(r, "Mar–May vs Jun–Aug units",
            new(2026, 3, 1), new(2026, 5, 31), new(2026, 6, 1), new(2026, 8, 30), minBaseline: MinUnitsForRanking, maxChange: -0.20);

        var alertFirers = Catalog.All
            .Select(p => (p.Sku, Change: Change(p.Sku, new(2026, 7, 6), new(2026, 8, 2), new(2026, 8, 3), new(2026, 8, 30), 60)))
            .Where(x => x.Change is < -0.30)
            .ToList();
        r.Add(new("Declining product", "As of Aug 31, product-units alert (−30%, 4 wks, min 60) fires for the candle only",
            string.Join(", ", alertFirers.Select(x => $"{x.Sku} {Pct(x.Change!.Value)}")) is { Length: > 0 } s ? s : "none",
            alertFirers.Count == 1 && alertFirers[0].Sku == DecliningSku));

        var candleRevenue = _sim.Orders
            .Where(o => InRange(o.BusinessDate, DeclineStart, DeclineEnd))
            .SelectMany(o => o.Lines)
            .Where(l => l.Product.Sku == DecliningSku)
            .Sum(l => l.LineTotalCents);
        var summerTotal = Total(DeclineStart, DeclineEnd);
        var share = candleRevenue / (double)summerTotal;
        r.Add(new("Declining product", "Candle is <5% of Jun–Aug revenue (stays hidden in headline cards)", Pct(share, 2), share < 0.05));

        // 5. Conversion drop
        var pre = Conversion(ConversionDropStart.AddDays(-28), ConversionDropStart.AddDays(-1));
        var post = Conversion(ConversionDropStart, ConversionDropStart.AddDays(27));
        r.Add(new("Conversion drop", "4 weeks before Jul 13: conversion 2.1–2.7%", Pct(pre, 2), pre is >= 0.021 and <= 0.027));
        r.Add(new("Conversion drop", "4 weeks after Jul 13: conversion 1.35–1.85%", Pct(post, 2), post is >= 0.0135 and <= 0.0185));
        var sessPre = Sessions(ConversionDropStart.AddDays(-28), ConversionDropStart.AddDays(-1));
        var sessPost = Sessions(ConversionDropStart, ConversionDropStart.AddDays(27));
        var sessChange = sessPost / (double)sessPre - 1;
        r.Add(new("Conversion drop", "Sessions flat across the change (±8%)", Pct(sessChange), Math.Abs(sessChange) <= 0.08));

        var falseAlarms = new List<DateOnly>();
        for (var end = DataStart.AddDays(6); end < ConversionDropStart; end = end.AddDays(1))
        {
            if (Conversion(end.AddDays(-6), end) < 0.020)
            {
                falseAlarms.Add(end);
            }
        }

        r.Add(new("Conversion drop", "No 7-day window before Jul 13 is below 2%",
            falseAlarms.Count == 0 ? "none" : $"{falseAlarms.Count} (first {falseAlarms[0]:yyyy-MM-dd})", falseAlarms.Count == 0));

        return r;
    }

    public string Summary()
    {
        var inStore = _sim.Orders.Where(o => o.Channel == SimChannel.InStore).ToList();
        var online = _sim.Orders.Where(o => o.Channel == SimChannel.Online).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(Inv, $"| Channel | Orders | Revenue | AOV | Orders/day |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (name, list) in new[] { ("In-store", inStore), ("Online", online) })
        {
            var rev = list.Sum(o => o.SubtotalCents);
            sb.AppendLine(Inv, $"| {name} | {list.Count:N0} | {Money(rev)} | {Money(rev / Math.Max(1, list.Count))} | {list.Count / 364.0:0.0} |");
        }

        var sessions = _sim.Traffic.Sum(t => t.Sessions);
        sb.AppendLine();
        sb.AppendLine(Inv, $"Sessions: {sessions:N0} ({sessions / 364.0:0}/day). Full-year conversion: {Pct(online.Count / (double)sessions, 2)}.");
        return sb.ToString();
    }

    private void AddDeclineCheck(
        List<CheckResult> r, string label,
        DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd,
        int minBaseline, double maxChange)
    {
        var changes = Catalog.All
            .Select(p => (p.Sku, Change: Change(p.Sku, aStart, aEnd, bStart, bEnd, minBaseline)))
            .Where(x => x.Change.HasValue)
            .OrderBy(x => x.Change!.Value)
            .ToList();

        var worst = changes[0];
        var runnerUp = changes[1];
        var passed = worst.Sku == DecliningSku && worst.Change!.Value <= maxChange;
        r.Add(new("Declining product",
            $"{label}: candle is the steepest decline and ≤ {Pct(maxChange)}",
            $"{worst.Sku} {Pct(worst.Change!.Value)}; next {runnerUp.Sku} {Pct(runnerUp.Change!.Value)}",
            passed));
    }

    private double? Change(string sku, DateOnly aStart, DateOnly aEnd, DateOnly bStart, DateOnly bEnd, int minBaseline)
    {
        var a = Units(sku, aStart, aEnd);
        if (a < minBaseline)
        {
            return null;
        }

        return Units(sku, bStart, bEnd) / (double)a - 1;
    }

    private int Units(string sku, DateOnly start, DateOnly end) =>
        Days(start, end).Sum(d => _units.GetValueOrDefault((sku, d)));

    private long InStore(DateOnly start, DateOnly end) =>
        Days(start, end).Sum(d => _revenue.GetValueOrDefault((SimChannel.InStore, d)));

    private long Total(DateOnly start, DateOnly end) =>
        Days(start, end).Sum(d => _revenue.GetValueOrDefault((SimChannel.InStore, d)) + _revenue.GetValueOrDefault((SimChannel.Online, d)));

    private int OnlineOrders(DateOnly start, DateOnly end) => Days(start, end).Sum(d => _onlineOrders.GetValueOrDefault(d));

    private int Sessions(DateOnly start, DateOnly end) => Days(start, end).Sum(d => _sessions.GetValueOrDefault(d));

    /// <summary>Σ orders ÷ Σ sessions — the same definition the API uses.</summary>
    private double Conversion(DateOnly start, DateOnly end) => OnlineOrders(start, end) / (double)Sessions(start, end);

    private static double AvgDaily(Func<DateOnly, DateOnly, long> sum, DateOnly start, DateOnly end) =>
        sum(start, end) / (double)(end.DayNumber - start.DayNumber + 1);

    private static IEnumerable<DateOnly> Days(DateOnly start, DateOnly end)
    {
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            yield return d;
        }
    }

    private static void Add<TKey>(Dictionary<TKey, long> dict, TKey key, long value)
        where TKey : notnull => dict[key] = dict.GetValueOrDefault(key) + value;

    private static string Money(long cents) => (cents / 100m).ToString("$#,##0", Inv);

    private static string Pct(double value, int decimals = 0) =>
        (value * 100).ToString(decimals == 0 ? "+0;-0;0" : "0.00", Inv) + "%";
}
