namespace LandDoc.Api.Model;

/// <summary>
/// The <c>GET /usage</c> response (spec 0009): the echoed <see cref="Range"/> + resolved window, token
/// totals and per-deployment usage with computed estimated cost, plus request and latency summaries.
/// </summary>
public sealed record UsageReport(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    UsageTotals Totals,
    IReadOnlyList<DeploymentUsageReport> Deployments,
    RequestSummary Requests,
    LatencySummary Latency);
