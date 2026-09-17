namespace MondayBrief.Seed;

public enum SimChannel
{
    InStore,
    Online,
}

public sealed record SimLine(SeedProduct Product, int Quantity)
{
    public long LineTotalCents => Product.PriceCents * Quantity;
}

public sealed class SimOrder
{
    public required SimChannel Channel { get; init; }
    public required string ExternalId { get; set; }
    public required DateTime LocalTime { get; init; }
    public required DateOnly BusinessDate { get; init; }
    public required IReadOnlyList<SimLine> Lines { get; init; }
    public string Register { get; set; } = "";
    public long SubtotalCents => Lines.Sum(l => l.LineTotalCents);
}

public sealed record SimTrafffic(DateOnly Date, int Sessions, int Users, int PageViews);

public sealed record Simulation(IReadOnlyList<SimOrder> Orders, IReadOnlyList<SimTrafffic> Traffic);