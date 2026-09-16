namespace MondayBrief.Core.Entities;

public enum AlertRuleKind
{
    /// <summary>Total revenue for the last WindowDays vs the WindowDays before, in percent. Fires when change &lt; Threshold.</summary>
    RevenueChangePct,

    /// <summary>Online conversion rate (percent) over the last WindowDays. Fires when rate &lt; Threshold.</summary>
    ConversionRateBelow,

    /// <summary>Per-product units for last WindowDays vs the WindowDays before, in percent. Fires when change &lt; Threshold.</summary>
    ProductUnitsChangePct,
}

/// <summary>A threshold rule. Triggered alerts are computed on request; there is no events table.</summary>
public sealed class AlertRule
{
    public int Id { get; set; }

    /// <summary>Stable slug, e.g. "weekly-revenue drop".</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public AlertRuleKind Kind { get; set; }

    /// <summary>Threshold in percent units (-20 means "down more than 20%"; 2.0 means "below 2%").</summary>
    public double Threshold { get; set; }

    public int WindowDays { get; set; }

    /// <summary>
    /// Minimum baseline before the rule is evaluated: units in the prior window for product rules,
    /// sessions in the window for conversion rules. Keeps low-volume noise from firing alerts. 0 = no minimum.
    /// </summary>
    public int MinBaseline { get; set; }

    public bool IsEnabled { get; set; } = true;
}