using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;

using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Kpis;

/// <summary>
/// Chart data. Every bucket in the range gets a point, including closed days: a missing point would 
/// draw a straight line across the storm closure and hide the most visible event in the data.
/// </summary>
public sealed class TimeSeriesService(MondayBriefDbContext db)
{
    private sealed record DailyRow(DateOnly Date, int ChannelId, long RevenueCents, int Orders);

    public async Task<TimeSeriesResult> GetAsync(TimeSeriesRequest request, CancellationToken cancellationToken = default)
    {
        var range = request.Range;

        var daily = await db.Orders
            .Where(o => o.BusinessDate >= range.Start && o.BusinessDate <= range.End)
            .GroupBy(o => new { o.BusinessDate, o.ChannelId })
            .Select(g => new DailyRow(
                g.Key.BusinessDate,
                g.Key.ChannelId,
                g.Sum(o => o.SubtotalCents),
                g.Count()))
            .ToListAsync(cancellationToken);

        var sessions = await db.DailyTraffic
            .Where(t => t.Date >= range.Start && t.Date <= range.End)
            .ToDictionaryAsync(t => t.Date, t => t.Sessions, cancellationToken);

        var channels = await db.Channels.OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var buckets = BucketStarts(range, request.Bucket);

        var series = request.ByChannel
            ? channels.Select(c => BuildSeries(c.Code, c.Name, buckets, request, daily.Where(d => d.ChannelId == c.Id).ToList(), sessions)).ToList()
            : [BuildSeries("all", "All channels", buckets, request, daily, sessions)];

        return new TimeSeriesResult(
            new RangeInfo(range.Start, range.End, range.Days, range.Previous().Start, range.Previous().End),
            request.Metric.ToString().ToLowerInvariant(),
            request.Bucket.ToString().ToLowerInvariant(),
            UnitOf(request.Metric),
            series);
    }

    private static MetricSeries BuildSeries(
        string key,
        string label,
        IReadOnlyList<DateOnly> buckets,
        TimeSeriesRequest request,
        IReadOnlyList<DailyRow> rows,
        IReadOnlyDictionary<DateOnly, int> sessions)
    {
        var revenue = new Dictionary<DateOnly, long>();
        var orders = new Dictionary<DateOnly, int>();
        var onlineOrders = new Dictionary<DateOnly, int>();
        var bucketSessions = new Dictionary<DateOnly, int>();

        foreach (var row in rows)
        {
            var bucket = BucketOf(row.Date, request.Bucket);
            revenue[bucket] = revenue.GetValueOrDefault(bucket) + row.RevenueCents;
            orders[bucket] = orders.GetValueOrDefault(bucket) + row.Orders;

            if (row.ChannelId == ChannelIds.Online)
            {
                onlineOrders[bucket] = onlineOrders.GetValueOrDefault(bucket) + row.Orders;
            }
        }

        foreach (var (date, count) in sessions)
        {
            var bucket = BucketOf(date, request.Bucket);
            bucketSessions[bucket] = bucketSessions.GetValueOrDefault(bucket) + count;
        }

        var points = buckets.Select(bucket =>
        {
            var bucketRevenue = revenue.GetValueOrDefault(bucket);
            var bucketOrders = orders.GetValueOrDefault(bucket);
            var value = request.Metric switch
            {
                SeriesMetric.Revenue => Round(bucketRevenue / 100m),
                SeriesMetric.Orders => bucketOrders,
                SeriesMetric.Aov => bucketOrders == 0 ? 0m : Round(bucketRevenue / 100m / bucketOrders),
                SeriesMetric.Sessions => bucketSessions.GetValueOrDefault(bucket),
                SeriesMetric.Conversion => Conversion(onlineOrders.GetValueOrDefault(bucket), bucketSessions.GetValueOrDefault(bucket)),
                _ => 0m,
            };

            return new SeriesPoint(bucket, value);
        }).ToList();

        return new MetricSeries(key, label, points);
    }

    /// <summary>Σ online orders ÷ Σ sessions within the bucket - never an average of daily rates.</summary>
    private static decimal Conversion(int onlineOrders, int sessions) =>
        sessions == 0 ? 0m : Round(100m * onlineOrders / sessions);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string UnitOf(SeriesMetric metric) => metric switch
    {
        SeriesMetric.Revenue or SeriesMetric.Aov => "currency",
        SeriesMetric.Conversion => "percent",
        _ => "number",
    };

    private static List<DateOnly> BucketStarts(DateRange range, SeriesBucket bucket)
    {
        var starts = new List<DateOnly>();

        if (bucket == SeriesBucket.Day)
        {
            for (var d = range.Start; d <= range.End; d = d.AddDays(1))
            {
                starts.Add(d);
            }

            return starts;
        }

        for (var week = MondayOnOrBefore(range.Start); week <= range.End; week = week.AddDays(7))
        {
            starts.Add(week);
        }

        return starts;
    }

    private static DateOnly BucketOf(DateOnly date, SeriesBucket bucket) =>
        bucket == SeriesBucket.Day ? date : MondayOnOrBefore(date);

    private static DateOnly MondayOnOrBefore(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}