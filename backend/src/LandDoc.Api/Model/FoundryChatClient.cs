namespace LandDoc.Api.Model;

/// <summary>
/// Microsoft Foundry gateway chat adapter — the production-primary provider (ADR-0002/0007). Slice
/// stub: the acceptance tests inject a fake <see cref="IChatClient"/>, so this is never called in
/// tests. No Foundry/Azure calls here (out of scope for the slice). Wired by config so the provider
/// switch stays config-only.
/// </summary>
public sealed class FoundryChatClient : IChatClient
{
    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FoundryChatClient is not implemented in the slice.");

    public Task<string> AnswerAsync(string question, IReadOnlyList<QaPassage> context, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FoundryChatClient is not implemented in the slice.");
}
