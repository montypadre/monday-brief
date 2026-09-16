namespace MondayBrief.Core.Entities;

/// <summary>
/// A generated weekly summary covering the Monday-to-Sunday week starting at <see cref="WeekStart"/>,
/// compared against the week before.
/// </summary>
public sealed class Brief
{
    public int Id { get; set; }

    /// <summary>The Monday the covered week starts on. One brief per week.</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>Structured model output: headline, whats_up, whats_down, watch_list, one_action.</summary>
    public required string Json { get; set; }

    /// <summary>The rendered 120-160 word text shown in the UI and the WordPress block.</summary>
    public required string RenderedText { get; set; }

    /// <summary>Model ID that produced this brief.</summary>
    public required string Model { get; set; }

    public DateTime CreatedUtc { get; set; }
}