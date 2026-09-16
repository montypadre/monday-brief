namespace MondayBrief.Core.Entities;

public sealed class OrderLine
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>Price actually charged per unit, in cents.<summary>
    public long UnitPriceCents { get; set; }

    public long LineTotalCents { get; set; }
}