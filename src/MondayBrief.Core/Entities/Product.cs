namespace MondayBrief.Core.Entities;

/// <summary>
/// A sellable item. The POS SKU is the canonical key; e-commerce line items map to it through their sku field.
/// Deliberately has no cost or margin data: "What's my profit margin?" must be a refusal case in the evals.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    /// <summary>One of: Coffee Bar, Local GOods, Home &amp; Gifts, Apparel.</summary>
    public required string Category { get; set; }

    /// <summary>Current list price in cents. Historical prices live on OrderLine</summary>
    public long ListPriceCents { get; set; }

    /// <summary>True when the product appears in the e-commerce feed. Coffee Bar items are in-store only.</summary>
    public bool SoldOnline { get; set; }

    public List<OrderLine> OrderLines { get; set; } = [];
}