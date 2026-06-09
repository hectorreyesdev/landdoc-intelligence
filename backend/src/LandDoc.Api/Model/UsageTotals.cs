namespace LandDoc.Api.Model;

/// <summary>Window token totals with the computed estimated cost (spec 0009 response).</summary>
public sealed record UsageTotals(long PromptTokens, long CompletionTokens, long TotalTokens, decimal EstimatedCostUsd);
