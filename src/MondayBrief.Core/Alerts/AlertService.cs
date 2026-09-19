using Microsoft.EntityFrameworkCore;
using MondayBrief.Core.Data;
using MondayBrief.Core.Entities;
using MondayBrief.Core.Kpis;

namespace MondayBrief.Core.Alerts;

/// <summary>
/// Evaluates the stored threshold rules on demand. Nothing is persisted: an alert is a fact about a persisted: an alert is a fact about a
/// window, so it is recomputed from the same data the dashboard and the AI read.
/// </summary>
public sealed class AlertService(MondayBriefDbContext db, KpiService kpis)
{
    /// <summary>Each rule over its own window, ending on the last complete day before <paramref name="asOf"/>.</summary>
    public async Task<IReadOnlyList<TriggeredAlert>> EvaluateAsOfAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var rules = await EnabledRulesAsync(cancellationToken);
        var alerts = new List<TriggeredAlert>();

        foreach (var rule in rules)
        {
            var window = DateRange.LastDays(asOf, rule.WindowDays);
            alerts.AddRange(await EvaluateAsync(rule, window, cancellationToken));
        }

        return alerts;
    }

    /// <summary>Every rule over one explicit window, for questions like "what went wrong in July?".</summary>
    public async Task<IReadOnlyList<TriggeredAlert>> EvaluateWindowAsync(
        DateRange window, CancellationToken cancellationToken = default)
    {
        var rules = await EnabledRulesAsync(cancellationToken);
        var alerts = new List<TriggeredAlert>();

        foreach (var rule in rules)
        {
            alerts.AddRange(await EvaluateAsync(rule, window, cancellationToken));
        }

        return alerts;
    }

    private async Task<List<AlertRule>> EnabledRulesAsync(CancellationToken cancellationToken) =>
        await db.AlertRules.Where(r => r.IsEnabled).OrderBy(r => r.Id).ToListAsync(cancellationToken);

    private Task<List<TriggeredAlert>> EvaluateAsync(AlertRule rule, DateRange window, CancellationToken cancellationToken) =>
        rule.Kind switch
        {
            AlertRuleKind.RevenueChangePct => RevenueChangeAsync(rule, window, cancellationToken),
            AlertRuleKind.ConversionRateBelow => ConversionAsync(rule, window, cancellationToken),
            AlertRuleKind.ProductUnitsChangePct => ProductUnitsAsync(rule, window, cancellationToken),
            _ => Task.FromResult(new List<TriggeredAlert>()),
        };

    private async Task<List<TriggeredAlert>> RevenueChangeAsync(
        AlertRule rule, DateRange window, CancellationToken cancellationToken)
    {
        var previous = window.Previous();
        var current = await kpis.GetTotalsAsync(window, null, cancellationToken);
        var before = await kpis.GetTotalsAsync(previous, null, cancellationToken);

        if (before.RevenueCents == 0)
        {
            return [];
        }

        var changePct = Math.Round(
            100m * (current.RevenueCents - before.RevenueCents) / before.RevenueCents, 1, MidpointRounding.AwayFromZero);
        
        if (changePct >= (decimal)rule.Threshold)
        {
            return [];
        }

        var currentDollars = Math.Round(current.RevenueCents / 100m, 2, MidpointRounding.AwayFromZero);
        var beforeDollars = Math.Round(before.RevenueCents / 100m, 2, MidpointRounding.AwayFromZero);

        return
        [
            new TriggeredAlert(
                rule.Key, rule.Name, "Total revenue", "revenue", changePct, (decimal)rule.Threshold, "percent",
                window.Start, window.End, previous.Start, previous.End,
                $"Revenue for {window} was {currentDollars:C}, {changePct}% against {beforeDollars:C} the {window.Days} days before."),
        ];
    }

    private async Task<List<TriggeredAlert>> ConversionAsync(
        AlertRule rule, DateRange window, CancellationToken cancellationToken)
    {
        var totals = await kpis.GetTotalsAsync(window, null, cancellationToken);

        if (totals.Sessions < rule.MinBaseline)
        {
            return [];
        }

        var rate = Math.Round((decimal)totals.ConversionPct, 2, MidpointRounding.AwayFromZero);
        if (rate >= (decimal)rule.Threshold)
        {
            return [];
        }

        return
        [
            new TriggeredAlert(
                rule.Key, rule.Name, "Online store", "conversion", rate, (decimal)rule.Threshold, "percent",
                window.Start, window.End, null, null,
                $"Online conversion for {window} was {rate}% from {totals.Sessions:N0} sessions, below the {rule.Threshold}% threshold."),
        ];
    }

    private async Task<List<TriggeredAlert>> ProductUnitsAsync(
        AlertRule rule, DateRange window, CancellationToken cancellationToken)
    {
        var previous = window.Previous();
        var current = await kpis.GetProductTotalsAsync(window, cancellationToken);
        var before = await kpis.GetProductTotalsAsync(previous, cancellationToken);
        var currentBySku = current.ToDictionary(p => p.Sku, StringComparer.Ordinal);

        var alerts = new List<TriggeredAlert>();

        foreach (var product in before.Where(p => p.Units >= rule.MinBaseline).OrderBy(p => p.Sku, StringComparer.Ordinal))
        {
            var units = currentBySku.TryGetValue(product.Sku, out var row) ? row.Units : 0;
            var changePct = Math.Round(100m * (units - product.Units) / product.Units, 1, MidpointRounding.AwayFromZero);

            if (changePct >= (decimal)rule.Threshold)
            {
                continue;
            }

            alerts.Add(new TriggeredAlert(
                rule.Key, rule.Name, product.Name, "units", changePct, (decimal)rule.Threshold, "percent",
                window.Start, window.End, previous.Start, previous.End,
                $"{product.Name} ({product.Sku}) sold {units} units in {window}, {changePct}% against {product.Units} the {window.Days} days before."));
        }

        return [.. alerts.OrderBy(a => a.Value)];
    }
    
}