namespace MondayBrief.Core.Kpis;

public enum SeriesMetric
{
    Revenue,
    Orders,
    Aov,
    Sessions,
    Conversion,
}

public enum SeriesBucket
{
    Day,
    Week,
}

public sealed record SeriesPoint(DateOnly Date, decimal Value);

public sealed record MetricSeries(string Key, string Label, IReadOnlyList<SeriesPoint> Points);

public sealed record TimeSeriesResult(
    RangeInfo Range,
    string Metric,
    string Bucket,
    string Unit,
    IReadOnlyList<MetricSeries> Series);
    
/// <summary>A validated /api/timeseries request.</summary>
public sealed record TimeSeriesRequest(DateRange Range, SeriesMetric Metric, SeriesBucket Bucket, bool ByChannel)
{
    public static bool TryParse(
        string? metric, string? by, string? bucket, string? range, DateOnly asOf,
        out TimeSeriesRequest request, out string? error)
    {
        request = null!;

        if (!RangeParser.TryParse(range, asOf, out var dateRange, out error))
        {
            return false;
        }

        if(!Enum.TryParse<SeriesMetric>(metric ?? "revenue", ignoreCase: true, out var parsedMetric))
        {
            error = $"Metric '{metric}' is not available. Use revenue, orders, aov, sessions or conversions.";
            return false;
        }

        if (!Enum.TryParse<SeriesBucket>(bucket ?? "day", ignoreCase: true, out var parsedBucket))
        {
            error = $"Bucket '{bucket}' is not available. Use day or week.";
            return false;
        }

        var byChannel = string.Equals(by, "channel", StringComparison.OrdinalIgnoreCase);
        if (!byChannel && !string.IsNullOrWhiteSpace(by) && !string.Equals(by, "none", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Split '{by}' is not available. Use channel, or leave it out.";
            return false;
        }

        // Web analytics has no channel dimension, so splitting these would be a lie rather than a gap.
        if (byChannel && parsedMetric is SeriesMetric.Sessions or SeriesMetric.Conversion)
        {
            error = $"{parsedMetric} cannot be split by channel: it comes from web analytics, which only covers the online store.";
            return false;
        }

        request = new TimeSeriesRequest(dateRange, parsedMetric, parsedBucket, byChannel);
        error = null;
        return true;
    }
}