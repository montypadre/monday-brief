namespace MondayBrief.Core.Entities;

/// <summary>Fixed IDs for the two sales channels. The Coffee Bar is a product category, not a channel.</summary>
public static class ChannelIds
{
    public const int InStore = 1;
    public const int Online = 2;
}

public sealed class Channel
{
    public int Id { get; set; }

    /// <summary>Stable code used by the API and the AI tools: "InStore" or "Online".</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public List<Order> Orders { get; set; } = [];
}