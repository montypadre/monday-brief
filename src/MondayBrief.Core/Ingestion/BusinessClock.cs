namespace MondayBrief.Core.Ingestion;

/// <summary>
/// The single place that converts between the business's local time and UTC.
/// Every adapter uses this, so they cannot disagree about which day an order belongs to.
/// </summary>
public sealed class BusinessClock(TimeZoneInfo zone)
{
    public static BusinessClock FromId(string timeZoneId) => new(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    public TimeZoneInfo Zone { get; } = zone;

    /// <summary>For sources that record wall-clock time with no offset (e.g. a POS terminal).</summary>
    public DateTime LocalToUtc(DateTime local) => 
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
    
    /// <summary>For sources that record UTC (e.g. an e-commerce paltform).</summary>
    public DateOnly BusinessDateOf(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Expected a UTC timestamp.", nameof(utc));
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    }
}