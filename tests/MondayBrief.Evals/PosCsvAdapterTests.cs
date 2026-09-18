using MondayBrief.Core.Entities;
using MondayBrief.Core.Ingestion;
using MondayBrief.Core.Ingestion.Adapters;
using Xunit;

namespace MondayBrief.Evals;

public sealed class PosCsvAdapterTests : IDisposable
{
    private const string Catalog = """
        SKU,Item Name,Department,Retail Price,Active
        LG-CNDL-01,Magnolia Bay Soy Candle,Local Goods,24.00,Y
        CB-DRIP-12,"Drip Coffee, 12 oz",Coffee Bar,3.25,Y
        """;

    private const string Transactions = """
        Transaction ID,Timestamp,Register,SKU,Item Name,Department,Qty,Unit Price,Line Total
        T250901-0001,9/1/2025 7:00:13 AM,REG1,CB-DRIP-12,"Drip Coffee, 12 oz",Coffee Bar,2,3.25,6.50
        T250901-0001,9/1/2025 7:00:13 AM,REG1,LG-CNDL-01,Magnolia Bay Soy Candle,Local Goods,1,24.00,24.00
        T251213-0004,12/13/2025 7:03:06 AM,REG2,LG-CNDL-01,Magnolia Bay Soy Candle,Local Goods,1,24.00,24.00
        """;
    
    private readonly string _dir = Directory.CreateTempSubdirectory("pos-adapter-tests").FullName;
    private readonly PosCsvAdapter _adapter = new(BusinessClock.FromId("America/Chicago"));

    private void WriteSource(string transactions = Transactions)
    {
        File.WriteAllText(Path.Combine(_dir, PosCsvAdapter.CatalogFileName), Catalog);
        File.WriteAllText(Path.Combine(_dir, PosCsvAdapter.TransactionsFileName), transactions);
    }

    [Fact]
    public void CanRead_is_false_when_the_files_are_missing()
    {
        Assert.False(_adapter.CanRead(_dir));
    }

    [Fact]
    public async Task Catalog_prices_become_cents_and_department_becomes_category()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        var candle = batch.Products.Single(p => p.Sku == "LG-CNDL-01");
        Assert.Equal(2400, candle.ListPriceCents);
        Assert.Equal("Local Goods", candle.Category);
        Assert.Equal("Drip Coffee, 12 oz", batch.Products.Single(p => p.Sku == "CB-DRIP-12").Name);
    }

    [Fact]
    public async Task Line_rows_are_grouped_into_orders()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        Assert.Equal(2, batch.Orders.Count);
        var first = batch.Orders[0];
        Assert.Equal("T250901-0001", first.ExternalId);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(3050, first.SubtotalCents);
        Assert.Equal(ChannelIds.InStore, first.ChannelId);
        Assert.Equal(SourceSystems.Pos, first.SourceSystem);
    }

    [Fact]
    public async Task Local_timestamps_convert_using_the_right_offset()
    {
        WriteSource();
        var batch = await _adapter.ReadAsync(_dir);

        // 7:00 AM CDT (summer, UTC-5) and 7:03 AM CST (winter, UTC-6).
        Assert.Equal(new DateTime(2025, 9, 1, 12, 0, 13, DateTimeKind.Utc), batch.Orders[0].PlacedUtc);
        Assert.Equal(new DateTime(2025, 12, 13, 13, 3, 6, DateTimeKind.Utc), batch.Orders[1].PlacedUtc);

        // Business date always comes from the register's own clock.
        Assert.Equal(new DateOnly(2025, 9, 1), batch.Orders[0].BusinessDate);
        Assert.Equal(new DateOnly(2025, 12, 13), batch.Orders[1].BusinessDate);
    }

    [Fact]
    public async Task Line_total_that_disagrees_with_quantity_is_rejected()
    {
        WriteSource("""
            Transaction ID,Timestamp,Register,SKU,Item Name,Department,Qty,Unit Price,Line Total,
            T1,9/1/2025 7:00:13 AM,REG1,CB-DRIP-12,Drip,Coffee Bar,2,3.25,3.25
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Equal(2, ex.Line);
        Assert.Contains("Line Total", ex.Message);
    }

    [Fact]
    public async Task Malformed_row_names_its_line_number()
    {
        WriteSource("""
            Transaction ID,Timestamp,Register,SKU,Item Name,Department,Qty,Unit Price,Line Total
            T1,9/1/2025 7:00:13 AM,REG1,CB-DRIP-12,Drip,Coffee Bar,1,3.25,3.25
            T2,not a timestamp,REG1,CB-DRIP-12,Drip,Coffee Bar,1,3.25,3.25
            """);

        var ex = await Assert.ThrowsAsync<SourceFormatException>(() => _adapter.ReadAsync(_dir));
        Assert.Equal(3, ex.Line);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}