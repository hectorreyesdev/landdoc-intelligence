using LandDoc.Api.Model;

namespace LandDoc.Api.Storage;

/// <summary>
/// Source of LLM usage telemetry for the ops dashboard (ADR-0020, spec 0009). Returns RAW aggregates
/// (tokens, requests, latency) for a window — NO cost; cost is computed provider-independently by the cost
/// calculator so both providers share it and it is unit-testable in isolation. Config-selected via
/// <c>UsageSource:Provider</c>: <c>azuremonitor</c> (live — Azure Monitor platform metrics) or
/// <c>inmemory</c> (offline/test). Registered as a singleton. Sibling to <see cref="IVectorStore"/> /
/// <see cref="IDocumentStore"/>.
/// </summary>
public interface IUsageSource
{
    /// <summary>
    /// Returns usage aggregates for <paramref name="range"/>. A no-data window yields all-zero aggregates
    /// (never throws for "empty").
    /// </summary>
    Task<UsageData> GetUsageAsync(UsageRange range, CancellationToken ct = default);
}
