namespace MondayBrief.Core.Entities;

public static class SourceSystems
{
    public const string Pos = "pos";
    public const string Ecommerce = "ecommerce";
}

/// <summary>
/// One sale from any source. Revenue = net sales excluding tax; discounts are already folded into line prices.
/// All KPIs group by <see cref="BusinessData"/>, never by <see cref="PlacedUtc"/>
/// </summary>
public sealed class Order
{
    public int Id { get; set; }

    public int ChannelId { get; set; }
    public Channel Channel { get; set; } = null!;

    /// <summary>Which adapter produced the row (see <see cref="SourceSystems"/>).</summary>
    public required string SourceSystem { get; set; }

    /// <summary>The ID in the source system. Unique per source, which makes re-ingestion idempotent.</summary>
    public required string ExternalId { get; set; }

    public DateTime PlacedUtc { get; set; }

    /// <summary>Calendar date in the business's time zone (American/Chicago).</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Sum of line totals, in cents. Stored so revenue queries don't need to join lines.</summary>
    public long SubtotalCents { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
}