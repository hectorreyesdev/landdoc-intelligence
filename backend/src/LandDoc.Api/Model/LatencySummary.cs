namespace LandDoc.Api.Model;

/// <summary>
/// Response latency for a window (spec 0009), from the <c>AzureOpenAITimeToResponse</c> metric: the average
/// and the maximum, in milliseconds. Latency is a rate, not a count — it does not scale with window length.
/// </summary>
public sealed record LatencySummary(double AvgMs, double MaxMs);
