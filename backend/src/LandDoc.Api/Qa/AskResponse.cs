namespace LandDoc.Api.Qa;

/// <summary>
/// Response body for <c>POST /ask</c> (spec 0002): a grounded <see cref="Answer"/> and the
/// <see cref="Citations"/> it came from (always ≥1 when the store is non-empty).
/// </summary>
public sealed record AskResponse(string Answer, IReadOnlyList<Citation> Citations);
