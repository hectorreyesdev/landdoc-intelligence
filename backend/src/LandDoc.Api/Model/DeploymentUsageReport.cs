namespace LandDoc.Api.Model;

/// <summary>Per-deployment usage with the computed estimated cost (spec 0009 response).</summary>
public sealed record DeploymentUsageReport(
    string Deployment, long PromptTokens, long CompletionTokens, long TotalTokens, decimal EstimatedCostUsd);
