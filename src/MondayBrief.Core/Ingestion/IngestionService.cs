using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Ingestion;

/// <summary>
/// Turns adapter output into rows. The only code that writes orders, so SKU resolution, the
/// SoldOnline flag and duplicate handling all live in one place.
/// </summary>
public sealed class IngestionService(MondayBriefDbContext db, IEnumerable<IDataSourceAdapter> adapters)
{
    private const int OrdersPerSave = 1_000;

    public async Task<IngestionReport> IngestAsync(string rawDataPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var batches = new List<SourceBatch>();
        var results = new List<SourceResult>();

        foreach (var adapter in adapters)
        {
            if (!adapter.CanRead(rawDataPath))
            {
                continue;
            }

            var batch = await adapter.ReadAsync(rawDataPath, cancellationToken);
            batches.Add(batch);
            results.Add(new SourceResult(
                adapter.SourceSystem,
                adapter.DisplayName,
                batch.Products.Count,
                batch.Orders.Count,
                batch.Traffic.Count));
        }

        if (batches.Count == 0)
        {
            throw new InvalidOperationException($"No source files found in '{rawDataPath}'. Run the seed generator first.");
        }

        var (productsInserted, productsUpdated, productIds) = await SyncProductsSync(batches, cancellationToken);
        var (ordersInserted, ordersSkipped, linesInserted) = await SyncOrdersAsync(batches, productIds, cancellationToken);
        var (trafficInserted, trafficUpdated) = await SyncTrafficAsync(batches, cancellationToken);

        stopwatch.Stop();

        return new IngestionReport(
            results,
            productsInserted,
            productsUpdated,
            ordersInserted,
            ordersSkipped,
            linesInserted,
            trafficInserted,
            trafficUpdated,
            stopwatch.Elapsed);
    }

    /// <summary>
    /// The POS catalog defines products. Whether a product is sold online is only knowable by looking
    /// aat the e-commerce orders, so that flag is derived here rather than by either adapter.
    /// </summary>
    private async Task<(int Inserted, int Updated, Dictionary<string, int> ProductIds)> SyncProductsSync(
        List<SourceBatch> batches, CancellationToken cancellationToken)
    {
        var sourceProducts = batches
            .SelectMany(b => b.Products)
            .GroupBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var onlineSkus = batches
            .SelectMany(b => b.Orders)
            .Where(o => o.ChannelId == ChannelIds.Online)
            .SelectMany(o => o.Lines)
            .Select(l => l.Sku)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await db.Products.ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var inserted = 0;
        var updated = 0;

        foreach (var (sku, source) in sourceProducts)
        {
            var soldOnline = onlineSkus.Contains(sku);

            if (existing.TryGetValue(sku, out var product))
            {
                if (product.Name != source.Name
                    || product.Category != source.Category
                    || product.ListPriceCents != source.ListPriceCents
                    || product.SoldOnline != soldOnline)
                {
                    product.Name = source.Name;
                    product.Category = source.Category;
                    product.ListPriceCents = source.ListPriceCents;
                    product.SoldOnline = soldOnline;
                    updated++;
                }

                continue;
            }

            product = new Product
            {
                Sku = sku,
                Name = source.Name,
                Category = source.Category,
                ListPriceCents = source.ListPriceCents,
                SoldOnline = soldOnline,
            };

            db.Products.Add(product);
            existing[sku] = product;
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return (inserted, updated, existing.ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<(int Inserted, int Skipped, int Lines)> SyncOrdersAsync(
        List<SourceBatch> batches, Dictionary<string, int> productIds, CancellationToken cancellationToken)
    {
        // One lookup of what is already stored makes re-running ingestion a no-op.
        var existingKeys = (await db.Orders
                .Select(o => new { o.SourceSystem, o.ExternalId })
                .ToListAsync(cancellationToken))
            .Select(o => Key(o.SourceSystem, o.ExternalId))
            .ToHashSet(StringComparer.Ordinal);

        var inserted = 0;
        var skipped = 0;
        var lines = 0;
        var pending = 0;

        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var sourceOrder in batches.SelectMany(b => b.Orders))
            {
                if (!existingKeys.Add(Key(sourceOrder.SourceSystem, sourceOrder.ExternalId)))
                {
                    skipped++;
                    continue;
                }

                var order = new Order
                {
                    ChannelId = sourceOrder.ChannelId,
                    SourceSystem = sourceOrder.SourceSystem,
                    ExternalId = sourceOrder.ExternalId,
                    PlacedUtc = sourceOrder.PlacedUtc,
                    BusinessDate = sourceOrder.BusinessDate,
                    SubtotalCents = sourceOrder.SubtotalCents,
                };

                foreach (var sourceLine in sourceOrder.Lines)
                {
                    if (!productIds.TryGetValue(sourceLine.Sku, out var productId))
                    {
                        throw new InvalidOperationException(
                            $"Order {sourceOrder.SourceSystem}/{sourceOrder.ExternalId} references SKU '{sourceLine.Sku}', " + 
                            "which is not in the POS catalog.");
                    }

                    order.Lines.Add(new OrderLine
                    {
                        ProductId = productId,
                        Quantity = sourceLine.Quantity,
                        UnitPriceCents = sourceLine.UnitPriceCents,
                        LineTotalCents = sourceLine.LineTotalCents,
                    });
                    lines++;
                }

                db.Orders.Add(order);
                inserted++;
                pending++;

                if (pending >= OrdersPerSave)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    pending = 0;
                }
            }

            if (pending > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;   
        }

        return (inserted, skipped, lines);
    }

    private async Task<(int Inserted, int Updated)> SyncTrafficAsync(
        List<SourceBatch> batches, CancellationToken cancellationToken)
    {
        var existing = await db.DailyTraffic.ToDictionaryAsync(t => t.Date, cancellationToken);

        var inserted = 0;
        var updated = 0;

        foreach (var source in batches.SelectMany(b => b.Traffic))
        {
            if (existing.TryGetValue(source.Date, out var row))
            {
                if (row.Sessions != source.Sessions || row.Users != source.Users || row.PageViews != source.PageViews)
                {
                    row.Sessions = source.Sessions;
                    row.Users = source.Users;
                    row.PageViews = source.PageViews;
                    updated++;
                }

                continue;
            }

            db.DailyTraffic.Add(new DailyTraffic
            {
                Date = source.Date,
                Sessions = source.Sessions,
                Users = source.Users,
                PageViews = source.PageViews,
            });
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (inserted, updated);
    }

    private static string Key(string sourceSystem, string externalId) => $"{sourceSystem}|{externalId}";
}