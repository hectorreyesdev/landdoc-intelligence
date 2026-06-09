namespace LandDoc.Api.Model;

/// <summary>
/// Raw usage aggregates for a window (spec 0009), returned by <c>IUsageSource</c> with NO cost — cost is
/// computed provider-independently by the cost calculator (ADR-0020). <see cref="From"/> / <see cref="To"/>
/// echo the resolved window. A no-data window yields all-zero aggregates (never an error).
/// </summary>
public sealed record UsageData(
    DateTimeOffset From,
    DateTimeOffset To,
    TokenAggregate Totals,
    IReadOnlyList<DeploymentUsage> Deployments,
    RequestSummary Requests,
    LatencySummary Latency);
