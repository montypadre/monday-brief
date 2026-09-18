using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Ingestion.Adapters;

/// <summary>
/// Reads the point-of-sale export: an item catalog plus a line-level transaction file.
/// Source quirks handled here: one row per line (not per sale), local wall-clock timestamps with no
/// offset, quoted fields containing commas, dollar amounts, and a "Department" column that is our Category.
/// </summary>
public sealed class PosCsvAdapter(BusinessClock clock) : IDataSourceAdapter
{
    public const string CatalogFileName = "pos_item_catalog.csv";
    public const string TransactionsFileName = "pos_transactions.csv";

    /// <summary>The register writes local time with no zone, e.g. 9/1/2025 7:00:13 AM.</summary>
    private const string TimestampFormat = "M/d/yyyy h:mm:ss tt";

    public string SourceSystem => SourceSystems.Pos;

    public string DisplayName => "Point of sale (CSV export)";

    public bool CanRead(string rawDataPath) =>
        File.Exists(Path.Combine(rawDataPath, CatalogFileName))
        && File.Exists(Path.Combine(rawDataPath, TransactionsFileName));

    public async Task<SourceBatch> ReadAsync(string rawDataPath, CancellationToken cancellationToken = default)
    {
        var products = await ReadCatalogAsync(Path.Combine(rawDataPath, CatalogFileName), cancellationToken);
        var orders = await ReadTransactionsAsync(Path.Combine(rawDataPath, TransactionsFileName), cancellationToken);
        return new SourceBatch(SourceSystem, products, orders, []);
    }

    private static async Task<List<SourceProduct>> ReadCatalogAsync(string path, CancellationToken cancellationToken)
    {
        var products = new List<SourceProduct>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var row in new CsvFile(path).ReadRowsAsync(cancellationToken))
        {
            var sku = row.Text("SKU");
            if (sku.Length == 0)
            {
                throw row.Error("SKU is empty.");
            }

            if (!seen.Add(sku))
            {
                throw row.Error($"Duplicate SKU '{sku}'.");
            }

            products.Add(new SourceProduct(
                Sku: sku,
                Name: row.Text("Item Name"),
                Category: row.Text("Department"),
                ListPriceCents: row.Cents("Retail Price")));
        }

        return products;
    }

    /// <summary>
    /// Groups line rows into orders by transaction ID. Rows for one transaction are contiguous in the 
    /// export, but grouping by ID does not rely on that.
    /// </summary>
    private async Task<List<SourceOrder>> ReadTransactionsAsync(string path, CancellationToken cancellationToken)
    {
        var builders = new Dictionary<string, OrderBuilder>(StringComparer.Ordinal);
        var order = new List<string>();

        await foreach (var row in new CsvFile(path).ReadRowsAsync(cancellationToken))
        {
            var transactionId = row.Text("Transaction ID");
            if (transactionId.Length == 0)
            {
                throw row.Error("Transaction ID is empty.");
            }

            var localTime = row.Timestamp("Timestamp", TimestampFormat);
            var quantity = row.Int("Qty");
            if (quantity <= 0)
            {
                throw row.Error($"Qty must be positive but was {quantity}.");
            }

            var unitPriceCents = row.Cents("Unit Price");
            var lineTotalCents = row.Cents("Line Total");
            if (unitPriceCents * quantity != lineTotalCents)
            {
                throw row.Error($"Line Total {lineTotalCents} does not equal Unit Price {unitPriceCents} x Qty {quantity}.");
            }

            if (!builders.TryGetValue(transactionId, out var builder))
            {
                builder = new OrderBuilder(localTime);
                builders.Add(transactionId, builder);
                order.Add(transactionId);
            }
            else if (localTime != builder.LocalTime)
            {
                throw row.Error($"Transaction '{transactionId}' has two different timestamps.");
            }

            builder.Lines.Add(new SourceOrderLine(
                Sku: row.Text("SKU"),
                Name: row.Text("Item Name"),
                Quantity: quantity,
                UnitPriceCents: unitPriceCents,
                LineTotalCents: lineTotalCents));
        }

        return order.Select(id =>
        {
            var builder = builders[id];
            return new SourceOrder(
                SourceSystem: SourceSystem,
                ExternalId: id,
                ChannelId: ChannelIds.InStore,
                // The register records local wall-clock time, so the business date is simply its date part.
                PlacedUtc: clock.LocalToUtc(builder.LocalTime),
                BusinessDate: DateOnly.FromDateTime(builder.LocalTime),
                Lines: builder.Lines);
        }).ToList();
    }

    private sealed class OrderBuilder(DateTime localTime)
    {
        public DateTime LocalTime { get; } = localTime;

        public List<SourceOrderLine> Lines { get; } = [];
    } 
}