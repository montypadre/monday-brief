using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Ingestion.Adapters;

/// <summary>
/// Reads the online store's order export. Source quirks handled here: snake_case keys, nested line 
/// items, UTC timestamps that must be converted to a business date, money as strings, and the
/// platform's own product IDs, which are ignored in favour of the POS SKU.
/// </summary>
public sealed class EcommerceJsonAdapter(BusinessClock clock) : IDataSourceAdapter
{
    public const string OrdersFileName = "ecommerce_orders.json";

    /// <summary>Only completed sales count as revenue; pending or refunded orders are skipped.</summary>
    private const string PaidStatus = "paid";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public string SourceSystem => SourceSystems.Ecommerce;

    public string DisplayName => "Online store (JSON export)";

    public bool CanRead(string rawDataPath) => File.Exists(Path.Combine(rawDataPath, OrdersFileName));

    public async Task<SourceBatch> ReadAsync(string rawDataPath, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rawDataPath, OrdersFileName);
        var export = await DeserializeAsync(path, cancellationToken);

        var orders = new List<SourceOrder>(export.Orders.Count);
        foreach (var order in export.Orders)
        {
            if (!string.Equals(order.FinancialStatus, PaidStatus, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            orders.Add(Convert(path, order));
        }

        // The online store has no catalog of its own: products and categories come from the POS export.
        return new SourceBatch(SourceSystem, [], orders, []);
    }

    private static async Task<EcommerceExport> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        try
        {
            return await JsonSerializer.DeserializeAsync<EcommerceExport>(stream, JsonOptions, cancellationToken)
                ?? throw new SourceFormatException(path, 0, "File contained no export object.");
        }
        catch (JsonException ex)
        {
            throw new SourceFormatException(path, (int)(ex.LineNumber ?? 0) + 1, ex.Message);
        }
    }

    private SourceOrder Convert(string path, EcommerceOrder order)
    {
        var id = order.Id.ToString(CultureInfo.InvariantCulture);

        if (order.LineItems.Count == 0)
        {
            throw Error(path, id, "Order has no line items.");
        }

        if (!DateTimeOffset.TryParse(order.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
        {
            throw Error(path, id, $"created_at is not an ISO-8601 timestamp: '{order.CreatedAt}'.");
        }

        var placedUtc = created.UtcDateTime;

        var lines = new List<SourceOrderLine>(order.LineItems.Count);
        foreach (var item in order.LineItems)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
            {
                throw Error(path, id, $"Line item '{item.Title}' has no sku; product_id {item.ProductId} cannot be matched.");
            }

            if (item.Quantity <= 0)
            {
                throw Error(path, id, $"Line item '{item.Sku}' has quantity {item.Quantity}.");
            }

            var unitPriceCents = ParseMoney(path, id, "price", item.Price);
            lines.Add(new SourceOrderLine(
                Sku: item.Sku.Trim(),
                Name: item.Title.Trim(),
                Quantity: item.Quantity,
                UnitPriceCents: unitPriceCents,
                LineTotalCents: unitPriceCents * item.Quantity));
        }

        var subtotalCents = ParseMoney(path, id, "subtotal_price", order.SubtotalPrice);
        var lineSum = lines.Sum(l => l.LineTotalCents);
        if (subtotalCents != lineSum)
        {
            throw Error(path, id, $"subtotal_price {subtotalCents} does not equal the sum of line items {lineSum}.");
        }

        return new SourceOrder(
            SourceSystem: SourceSystem,
            ExternalId: id,
            ChannelId: ChannelIds.Online,
            PlacedUtc: placedUtc,
            // The platform stamps UTC, so the business date needs the shop's time zone.
            BusinessDate: clock.BusinessDateOf(placedUtc),
            Lines: lines);
    }

    private static long ParseMoney(string path, string orderId, string field, string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)
            ? (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero)
            : throw Error(path, orderId, $"{field} is not an amount: '{value}'.");

    /// <summary>JSON has no useful line numbers once parse, so errors identify the order instead.</summary>
    private static SourceFormatException Error(string path, string orderId, string message) => 
        new(path, 0, $"order {orderId}: {message}");

    private sealed record EcommerceExport(
        [property: JsonPropertyName("orders")] IReadOnlyList<EcommerceOrder> Orders);

    
    private sealed record EcommerceOrder(
        long Id,
        string OrderNumber,
        string CreatedAt,
        string FinancialStatus,
        string SubtotalPrice,
        IReadOnlyList<EcommerceLineItem> LineItems);

    private sealed record EcommerceLineItem(
        long ProductId,
        string Sku,
        string Title,
        int Quantity,
        string Price);
}