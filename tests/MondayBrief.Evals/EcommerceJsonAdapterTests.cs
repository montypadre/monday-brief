using MondayBrief.Core.Entities;
using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;

using Xunit;

namespace MondayBrief.Evals;

public sealed class EcommerceJsonAdapterTests : IDisposable
{
    private const string Orders = """
        {
            "exported_at": "2026-08-31T11:00:00Z",
            "shop": "bayside-mercantile",
            "orders": [
                {
                    "id": 5001007,
                    "order_number": "#BM1008",
                    "created_at": "2026-08-12T02:58:00Z",
                    "currency": "USD",
                    "financial_status": "paid",
                    "subtotal_price": "72.00",
                    "line_items": [
                        { "product_id": 8810006, "sku": "LG-CNDL-01", "title": "Magnolia Bay Soy Candle", "quantity": 2, "price": "24.00" },
                        { "product_id": 8820004, "sku": "HG-TOWEL-01", "title": "Coastal Tea Towel", "quantity": 1, "price": "24.00" }
                    ]
                },
                {
                    "id": 5001008,
                    "order_number": "#BM1009",
                    "created_at": "2026-08-12T15:10:00Z",
                    "currency": "USD",
                    "financial_status": "refunded",
                    "subtotal_price": "26.00",
                    "line_items": [
                        { "product_id": 8830001, "sku": "AP-TEE-01", "title": "Bayside Logo Tee", "quantity": 1, "price": "26.00" }
                    ]
                }
            ]
        }
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("ecom-adapter-tests").FullName;
    private readonly EcommerceJsonAdapter _adapter = new(BusinessClock.FromId("America/Chicago"));

    private void WriteSource(string json = Orders) =>
        File.WriteAllText(Path.Combine(_dir, EcommerceJsonAdapter.OrdersFileName), json);

    [Fact]
    public async Task Line_items_are_matched_on_sku_not_platform_product_id()
    {
        WriteSource();
        var order = (await _adapter.ReadAsync(_dir)).Orders.Single();

        Assert.Equal("LG-CNDL-01|HG-TOWEL-01", string.Join("|", order.Lines.Select(l => l.Sku)));
        Assert.Equal(4800, order.Lines[0].LineTotalCents);
        Assert.Equal(7200, order.SubtotalCents);
        Assert.Equal(ChannelIds.Online, order.ChannelId);
        Assert.Equal(SourceSystems.Ecommerce, order.SourceSystem);
    }

    [Fact]
    public async Task Utc_timestamp_maps_to_the_previous_business_date()
    {
        WriteSource();
        var order = (await _adapter.ReadAsync(_dir)).Orders.Single();

        // 02:58 UTC on Aug 12 is 9:58 pm CDT on Aug 11 - the evening before the storm closure.
        Assert.Equal(new DateTime(2026, 8, 12, 2, 58, 0, DateTimeKind.Utc), order.PlacedUtc);
        Assert.Equal(new DateOnly(2026, 8, 11), order.BusinessDate);
    }

    [Fact]
    public async Task Orders_that_are_not_paid_are_skipped()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Single(batch.Orders);
        Assert.Equal("5001007", batch.Orders[0].ExternalId);
    }

    [Fact]
    public async Task The_online_export_contributes_no_products_or_traffic()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Empty(batch.Products);
        Assert.Empty(batch.Traffic);
    }

    [Fact]
    public async Task Subtotal_that_disagrees_with_line_items_is_rejected()
    {
        WriteSource("""
            {"orders":[{"id":99,"created_at":"2026-08-12T02:58:00Z","financial_status":"paid","subtotal_price":"50.00","line_items":[{"product_id":1,"sku":"LG-CNDL-01","title":"Candle","quantity":2,"price":"24.00"}]}]}
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Contains("order 99", ex.Message);
        Assert.Contains("subtotal_price", ex.Message);
    }

    [Fact]
    public async Task Line_item_without_a_sku_cannot_be_matched()
    {
        WriteSource("""
            {"orders":[{"id":99,"created_at":"2026-08-12T02:58:00Z","financial_status":"paid","subtotal_price":"24.00","line_items":[{"product_id":1,"sku":"","title":"Candle","quantity":1,"price":"24.00"}]}]}
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Contains("no sku", ex.Message);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}