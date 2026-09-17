using System.Data.Common;

namespace MondayBrief.Seed;

public enum ProductSeason
{
    None,
    HotDrink,
    ColdDrink,
    MardiGras,
    Holiday,
    Winter,

    /// <summary>The planted "hidden problem" product.</summary>
    Decline,
}

public sealed record SeedProduct(
    string Sku,
    string Name,
    string Category,
    long PriceCents,
    double InStoreWeight,
    double OnlineWeight,
    ProductSeason Season,
    long EcommerceProductId)
{
    public bool IsCoffeeBar => Category == Catalog.CoffeeBar;
    public bool SoldOnline => OnlineWeight > 0;
}

/// <summary>Bayside Mercantile's 24 products. Fictional. Prices are constant for the year.</summary>
public static class Catalog
{
    public const string CoffeeBar = "Coffee Bar";
    public const string LocalGoods = "Local Goods";
    public const string HomeGifts = "Home & Gifts";
    public const string Apparel = "Apparel";

    public static readonly IReadOnlyList<SeedProduct> All =
    [
        // Coffee Bar: in-store only (online weight 0). "Drip Coffee, 12 oz" has a comma on purpose to exercise CSV quoting.
        new("CB-DRIP-12", "Drip Coffee, 12 oz", CoffeeBar, 325, 3.0, 0, ProductSeason.HotDrink, 0),
        new("CB-LATTE-12", "Latte 12 oz", CoffeeBar, 525, 2.5, 0, ProductSeason.HotDrink, 0),
        new("CB-CHAI-12", "Chai Latte 12 oz", CoffeeBar, 500, 1.0, 0, ProductSeason.HotDrink, 0),
        new("CB-COLD-16", "Cold Brew 16 oz", CoffeeBar, 475, 1.5, 0, ProductSeason.ColdDrink, 0),
        new("CB-MUFF-01", "Praline Muffin", CoffeeBar, 375, 1.5, 0, ProductSeason.None, 0),
        new("CB-BEIG-03", "Beignets (3)", CoffeeBar, 450, 1.5, 0, ProductSeason.None, 0),

        new("LG-MARM-01", "Satsuma Marmalade", LocalGoods, 950, 6.0, 3.0, ProductSeason.None, 8810001),
        new("LG-HONEY-01", "Tupelo Honey 12 oz", LocalGoods, 1400, 5.0, 3.0, ProductSeason.None, 8810002),
        new("LG-PRALN-06", "Pecan Pralines (6-pack)", LocalGoods, 1200, 6.0, 5.0, ProductSeason.None, 8810003),
        new("LG-SALT-01", "Gulf Sea Salt", LocalGoods, 800, 3.0, 3.0, ProductSeason.None, 8810004),
        new("LG-HOTS-01", "Bayou Heat Hot Sauce", LocalGoods, 750, 4.0, 3.0, ProductSeason.None, 8810005),
        new("LG-CNDL-01", "Magnolia Bay Soy Candle", LocalGoods, 2400, 3.5, 3.5, ProductSeason.Decline, 8810006),

        new("HG-FRAME-01", "Driftwood Picture Frame", HomeGifts, 2800, 3.0, 3.0, ProductSeason.None, 8820001),
        new("HG-BEADS-01", "Mardi Gras Bead Garland", HomeGifts, 1600, 2.0, 2.0, ProductSeason.MardiGras, 8820002),
        new("HG-COAST-04", "Pelican Coaster Set (4)", HomeGifts, 2200, 4.0, 4.0, ProductSeason.None, 8820003),
        new("HG-TOWEL-01", "Coastal Tea Towel", HomeGifts, 1400, 4.0, 4.0, ProductSeason.None, 8820004),
        new("HG-ORNA-01", "Oyster Shell Ornament", HomeGifts, 1800, 1.0, 1.0, ProductSeason.Holiday, 8820005),
        new("HG-PRINT-01", "Marsh at Dusk Art Print", HomeGifts, 3500, 2.0, 4.0, ProductSeason.None, 8820006),

        new("AP-TEE-01", "Bayside Logo Tee", Apparel, 2600, 5.0, 6.0, ProductSeason.None, 8830001),
        new("AP-HOOD-01", "Eastern Shore Hoodie", Apparel, 4800, 2.0, 4.0, ProductSeason.Winter, 8830002),
        new("AP-HAT-01", "Pelican Trucker Hat", Apparel, 2400, 3.0, 4.0, ProductSeason.None, 8830003),
        new("AP-KIDS-01", "Kids Crab Tee", Apparel, 2000, 3.0, 3.0, ProductSeason.None, 8830004),
        new("AP-TOTE-01", "Canvas Beach Tote", Apparel, 3000, 3.0, 4.0, ProductSeason.None, 8830005),
        new("AP-KREWE-01", "Krewe Tee", Apparel, 2800, 1.5, 1.5, ProductSeason.MardiGras, 8830006),
    ];

    public static readonly IReadOnlyList<SeedProduct> CoffeeProducts = All.Where(p => p.IsCoffeeBar).ToList();
    public static readonly IReadOnlyList<SeedProduct> RetailProducts = All.Where(p => !p.IsCoffeeBar).ToList();
    public static readonly IReadOnlyList<SeedProduct> OnlineProducts = All.Where(p => p.SoldOnline).ToList();
}