using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace MondayBrief.Seed;

/// <summary>
/// Writes the three "outside systems" exports. Their formats differ on purpose so the ingestion
/// adapters have real normalization work to do:
/// POS         line-level CSV, local timestamps with no offset, keyed by SKU, dollar prices
/// E-commerce  nested JSON, UTC ISO-8601 timestamps, its own product IDs plus a sku field, prices as strings
/// Analytics   CSV with a comment preamble, yyyyMMdd dates, GA-style column names
/// </summary>
public static class SourceWriters
{
    public const string PosCatalogFile = "pos_item_catalog.csv";
    public const string PostTransactionsFile = "pos_transactions.csv";
    public const string EcommerceOrdersFile = "ecommerce_orders.json";
    public const string AnalyticsFile = "web_analytics_daily.csv";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WritePosCatalog(string directory)
    {
        var sb = new StringBuilder();
        sb.Append("SKU,Item Name, Department,Retail Price,Active\n");
        foreach (var p in Catalog.All)
        {
            sb.Append(Csv(p.Sku)).Append(',')
              .Append(Csv(p.Name)).Append(',')
              .Append(Csv(p.Category)).Append(',')
              .Append(Dollars(p.PriceCents)).Append(',')
              .Append("Y\n");
        }

        File.WriteAllText(Path.Combine(directory, PosCatalogFile), sb.ToString(), Utf8NoBom);

    }

    public static void WritePosTransactions(string directory, IEnumerable<SimOrder> orders)
    {
        using var writer = new StreamWriter(Path.Combine(directory, PostTransactionsFile), false, Utf8NoBom);
        writer.NewLine = "\n";
        writer.WriteLine("Transaction ID,Timestamp,Register,SKU,Item Name,Department,Qty,Unit Price,Line Total");
        foreach (var order in orders.Where(o => o.Channel == SimChannel.InStore))
        {
            var stamp = order.LocalTime.ToString("M/d/yyyy h:mm:ss tt", Inv);
            foreach (var line in order.Lines)
            {
                writer.Write(order.ExternalId); writer.Write(',');
                writer.Write(stamp); writer.Write(',');
                writer.Write(order.Register); writer.Write(',');
                writer.Write(Csv(line.Product.Sku)); writer.Write(',');
                writer.Write(Csv(line.Product.Name)); writer.Write(',');
                writer.Write(Csv(line.Product.Category)); writer.Write(',');
                writer.Write(Dollars(line.Product.PriceCents)); writer.Write(',');
                writer.WriteLine(Dollars(line.LineTotalCents));
            }
        }
    }

    public static void WriteEcommerceOrders(string directory, IEnumerable<SimOrder> orders, TimeZoneInfo zone)
    {
        var online = orders.Where(o => o.Channel == SimChannel.Online).ToList();
        var export = new EcomExport(
            ExportedAt: FormatUtc(ToUtc(PlantedEvents.AsOfDate.ToDateTime(new TimeOnly(6, 0)), zone)),
            Shop: "bayside-mercantile",
            Orders: online.Select((o, i) => new EcomOrder(
                Id: long.Parse(o.ExternalId, Inv),
                OrderNumber: $"#BM{1001 + i}",
                CreatedAt: FormatUtc(ToUtc(o.LocalTime, zone)),
                Currency: "USD",
                FinancialStatus: "paid",
                SubtotalPrice: Dollars(o.SubtotalCents),
                LineItems: o.Lines.Select(l => new EcomLineItem(
                    ProductId: l.Product.EcommerceProductId,
                    Sku: l.Product.Sku,
                    Title: l.Product.Name,
                    Quantity: l.Quantity,
                    Price: Dollars(l.Product.PriceCents))).ToList())).ToList());

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
        };

        using var stream = File.Create(Path.Combine(directory, EcommerceOrdersFile));
        JsonSerializer.Serialize(stream, export, options);
    }

    public static void WriteAnalytics(string directory, IEnumerable<SimTrafffic> traffic)
    {
        var rows = traffic.OrderBy(t => t.Date).ToList();
        var sb = new StringBuilder();
        sb.Append("# ----------------------------------------\n");
        sb.Append("# Bayside Mercantile - Web analytics export\n");
        sb.Append("# Report: Traffic by day\n");
        sb.Append(CultureInfo.InvariantCulture, $"# {rows[0].Date:yyyyMMdd}-{rows[^1].Date:yyyyMMdd}\n");
        sb.Append("# ----------------------------------------\n");
        sb.Append("Date,Sessions,Total users,Views\n");
        foreach (var t in rows)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{t.Date:yyyyMMdd},{t.Sessions},{t.Users},{t.PageViews}\n");
        }

        File.WriteAllText(Path.Combine(directory, AnalyticsFile), sb.ToString(), Utf8NoBom);
    }

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) => 
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    private static string FormatUtc(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv);

    private static string Dollars(long cents) => (cents / 100m).ToString("0.00", Inv);

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    
    private sealed record EcomExport(string ExportedAt, string Shop, IReadOnlyList<EcomOrder> Orders);

    private sealed record EcomOrder(
        long Id,
        string OrderNumber,
        string CreatedAt,
        string Currency,
        string FinancialStatus,
        string SubtotalPrice,
        IReadOnlyList<EcomLineItem> LineItems);

    private sealed record EcomLineItem(
        long ProductId,
        string Sku,
        string Title,
        int Quantity,
        string Price);
}