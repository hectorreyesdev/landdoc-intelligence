using LandDoc.Api.Model;

namespace LandDoc.Api.Storage;

/// <summary>
/// Offline/test usage source (ADR-0020): returns a canned <b>per-day</b> template scaled by the requested
/// range (so a wider range is a strict superset), with a real <see cref="UsageData.From"/> /
/// <see cref="UsageData.To"/> window. Pinned in tests via <c>UsageSource:Provider=inmemory</c> so CI needs
/// no Azure Monitor access, mirroring <see cref="InMemoryVectorStore"/> / <see cref="InMemoryDocumentStore"/>.
/// Construct with a custom per-day template (its From/To are ignored) to model an empty window → all-zero
/// aggregates.
/// </summary>
public sealed class InMemoryUsageSource : IUsageSource
{
    private readonly UsageData _perDay;

    public InMemoryUsageSource() : this(DefaultPerDay())
    {
    }

    public InMemoryUsageSource(UsageData perDay)
    {
        ArgumentNullException.ThrowIfNull(perDay);
        _perDay = perDay;
    }

    public Task<UsageData> GetUsageAsync(UsageRange range, CancellationToken ct = default)
    {
        var days = DaysIn(range);
        var to = DateTimeOffset.UtcNow;
        var from = to - TimeSpan.FromDays(days);

        // Tokens + request counts scale with the window length; latency is a rate and does not.
        var deployments = _perDay.Deployments
            .Select(d => Deployment(d.Deployment, d.PromptTokens * days, d.CompletionTokens * days))
            .ToList();

        var totals = new TokenAggregate(
            deployments.Sum(d => d.PromptTokens),
            deployments.Sum(d => d.CompletionTokens),
            deployments.Sum(d => d.TotalTokens));

        var r = _perDay.Requests;
        var requests = new RequestSummary(
            r.Total * days, r.Success * days, r.ClientErrors * days, r.Throttled429 * days, r.ServerErrors * days);

        return Task.FromResult(new UsageData(from, to, totals, deployments, requests, _perDay.Latency));
    }

    private static DeploymentUsage Deployment(string name, long prompt, long completion) =>
        new(name, prompt, completion, prompt + completion);

    private static int DaysIn(UsageRange range) => range switch
    {
        UsageRange.Last24h => 1,
        UsageRange.Last7d => 7,
        UsageRange.Last30d => 30,
        _ => 1,
    };

    // Canned, plausible per-day numbers for the two live deployments. From/To/Totals here are placeholders —
    // GetUsageAsync recomputes Totals from the (scaled) Deployments and stamps the real window.
    private static UsageData DefaultPerDay() => new(
        default,
        default,
        new TokenAggregate(0, 0, 0),
        [
            new DeploymentUsage("gpt-5.4-mini", 120_000, 30_000, 150_000),
            new DeploymentUsage("text-embedding-3-small", 50_000, 0, 50_000),
        ],
        new RequestSummary(Total: 400, Success: 380, ClientErrors: 8, Throttled429: 10, ServerErrors: 2),
        new LatencySummary(AvgMs: 850.0, MaxMs: 4200.0));
}
