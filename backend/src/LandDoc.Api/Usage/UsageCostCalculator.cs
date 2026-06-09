using LandDoc.Api.Model;

namespace LandDoc.Api.Usage;

/// <summary>
/// Pure, provider-independent cost estimator (ADR-0020, spec 0009): enriches raw <see cref="UsageData"/>
/// into a <see cref="UsageReport"/> by computing <c>estimatedCostUsd</c> from a per-deployment price table
/// (USD per 1,000 tokens, input/output applied separately). A deployment with no configured price contributes
/// $0 — an honest "estimate", not an error. Cost lives OUTSIDE the adapters so both providers share it and it
/// is unit-testable in isolation.
/// </summary>
public sealed class UsageCostCalculator
{
    private readonly IReadOnlyDictionary<string, DeploymentPrice> _prices;

    public UsageCostCalculator(IReadOnlyDictionary<string, DeploymentPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        // Deployment names from Azure Monitor are matched case-insensitively against the price table.
        _prices = new Dictionary<string, DeploymentPrice>(prices, StringComparer.OrdinalIgnoreCase);
    }

    public UsageReport ToReport(UsageData data, UsageRange range)
    {
        ArgumentNullException.ThrowIfNull(data);

        var deployments = data.Deployments
            .Select(d => new DeploymentUsageReport(
                d.Deployment, d.PromptTokens, d.CompletionTokens, d.TotalTokens, CostOf(d)))
            .ToList();

        var totals = new UsageTotals(
            data.Totals.PromptTokens,
            data.Totals.CompletionTokens,
            data.Totals.TotalTokens,
            deployments.Sum(d => d.EstimatedCostUsd));

        return new UsageReport(
            UsageRanges.ToWire(range), data.From, data.To, totals, deployments, data.Requests, data.Latency);
    }

    private decimal CostOf(DeploymentUsage d)
    {
        if (!_prices.TryGetValue(d.Deployment, out var price))
        {
            return 0m; // No configured price → excluded from the estimate (honest, not an error).
        }

        return (d.PromptTokens / 1000m * price.InputPer1K) + (d.CompletionTokens / 1000m * price.OutputPer1K);
    }
}
