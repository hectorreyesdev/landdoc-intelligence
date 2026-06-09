namespace LandDoc.Api.Model;

/// <summary>
/// Request volume + health for a window (spec 0009), bucketed from the <c>AzureOpenAIRequests</c> metric
/// split by <c>StatusCode</c>: <see cref="Success"/> = 2xx, <see cref="ClientErrors"/> = 4xx excluding 429,
/// <see cref="Throttled429"/> = 429, <see cref="ServerErrors"/> = 5xx. <see cref="Total"/> is all requests.
/// </summary>
public sealed record RequestSummary(long Total, long Success, long ClientErrors, long Throttled429, long ServerErrors);
