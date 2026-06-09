namespace LandDoc.Api.Model;

/// <summary>
/// Token counts for a window (spec 0009). <see cref="TotalTokens"/> is computed as
/// <see cref="PromptTokens"/> + <see cref="CompletionTokens"/> for internal consistency.
/// </summary>
public sealed record TokenAggregate(long PromptTokens, long CompletionTokens, long TotalTokens);
