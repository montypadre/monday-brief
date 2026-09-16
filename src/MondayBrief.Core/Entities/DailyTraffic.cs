namespace MondayBrief.Core.Entities;

/// <summary>
/// One row per day of website analytics.
/// Online conversion rate for a period = Σ online orders ÷ Σ sessions over that period.
/// It is never an average of daily rates. The evals depend on this definition.
/// </summary>
public sealed class DailyTraffic
{
    public DateOnly Date { get; set; }

    public int Sessions { get; set; }

    public int Users { get; set; }

    public int PageViews { get; set; }
}