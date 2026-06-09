using LandDoc.Api.Model;
using LandDoc.Api.Usage;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0009 / ADR-0020 — the pure <see cref="UsageCostCalculator"/>: tokens × a per-deployment price table
/// (input/output applied separately), totals = sum of per-deployment cost, a missing price → $0 (not an
/// error), and case-insensitive deployment matching.
/// </summary>
public sealed class UsageCostCalculatorTests
{
    private static readonly Dictionary<string, DeploymentPrice> Prices = new()
    {
        ["gpt-5.4-mini"] = new DeploymentPrice { InputPer1K = 0.00015m, OutputPer1K = 0.0006m },
        ["text-embedding-3-small"] = new DeploymentPrice { InputPer1K = 0.00002m, OutputPer1K = 0m },
    };

    private static UsageData Data(params DeploymentUsage[] deployments)
    {
        var totals = new TokenAggregate(
            deployments.Sum(d => d.PromptTokens),
            deployments.Sum(d => d.CompletionTokens),
            deployments.Sum(d => d.TotalTokens));
        return new UsageData(
            default, default, totals, deployments,
            new RequestSummary(5, 4, 1, 0, 0), new LatencySummary(120, 900));
    }

    [Fact]
    public void Cost_appliesInputAndOutputRatesSeparately_perDeployment()
    {
        var data = Data(
            new DeploymentUsage("gpt-5.4-mini", 100_000, 20_000, 120_000),
            new DeploymentUsage("text-embedding-3-small", 40_000, 0, 40_000));

        var report = new UsageCostCalculator(Prices).ToReport(data, UsageRange.Last24h);

        var gpt = report.Deployments.Single(d => d.Deployment == "gpt-5.4-mini");
        var emb = report.Deployments.Single(d => d.Deployment == "text-embedding-3-small");

        // 100k/1k*0.00015 + 20k/1k*0.0006 = 0.015 + 0.012 = 0.027
        Assert.Equal(0.027m, gpt.EstimatedCostUsd);
        // 40k/1k*0.00002 + 0 = 0.0008
        Assert.Equal(0.0008m, emb.EstimatedCostUsd);
        // totals = sum of per-deployment cost
        Assert.Equal(0.0278m, report.Totals.EstimatedCostUsd);
    }

    [Fact]
    public void Report_echoesRange_andPassesThroughTokensRequestsLatency()
    {
        var data = Data(new DeploymentUsage("gpt-5.4-mini", 1000, 1000, 2000));

        var report = new UsageCostCalculator(Prices).ToReport(data, UsageRange.Last7d);

        Assert.Equal("7d", report.Range);
        Assert.Equal(2000, report.Totals.TotalTokens);
        Assert.Equal(5, report.Requests.Total);
        Assert.Equal(120, report.Latency.AvgMs);
        Assert.Equal(900, report.Latency.MaxMs);
    }

    [Fact]
    public void Deployment_withNoConfiguredPrice_costsZero_notAnError()
    {
        var data = Data(new DeploymentUsage("mystery-model", 1_000_000, 1_000_000, 2_000_000));

        var report = new UsageCostCalculator(Prices).ToReport(data, UsageRange.Last24h);

        Assert.Equal(0m, report.Deployments.Single().EstimatedCostUsd);
        Assert.Equal(0m, report.Totals.EstimatedCostUsd);
    }

    [Fact]
    public void ZeroTokens_costZero()
    {
        var data = Data(new DeploymentUsage("gpt-5.4-mini", 0, 0, 0));

        var report = new UsageCostCalculator(Prices).ToReport(data, UsageRange.Last24h);

        Assert.Equal(0m, report.Totals.EstimatedCostUsd);
    }

    [Fact]
    public void DeploymentName_matchesPriceTable_caseInsensitively()
    {
        var data = Data(new DeploymentUsage("GPT-5.4-MINI", 100_000, 0, 100_000));

        var report = new UsageCostCalculator(Prices).ToReport(data, UsageRange.Last24h);

        // 100k/1k * 0.00015 = 0.015 — matched despite the upper-case deployment name.
        Assert.Equal(0.015m, report.Totals.EstimatedCostUsd);
    }
}
