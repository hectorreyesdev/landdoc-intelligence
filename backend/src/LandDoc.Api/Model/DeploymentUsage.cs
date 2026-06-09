namespace LandDoc.Api.Model;

/// <summary>
/// Raw per-deployment token counts for a window (spec 0009) — NO cost. Cost is layered on
/// provider-independently by the cost calculator (ADR-0020), producing a <see cref="DeploymentUsageReport"/>.
/// </summary>
public sealed record DeploymentUsage(string Deployment, long PromptTokens, long CompletionTokens, long TotalTokens);
