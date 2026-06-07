using LandDoc.Api.Storage;

namespace LandDoc.Api.Model;

/// <summary>
/// Microsoft Foundry gateway chat adapter — the primary provider (ADR-0002/0007). Not implemented in
/// the slice: the acceptance tests inject a fake <see cref="IChatClient"/> and both endpoints return
/// 501, so this is never called. No Foundry/Azure calls here (out of scope). Wired by config so the
/// provider switch stays config-only.
/// </summary>
public sealed class FoundryChatClient : IChatClient
{
    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FoundryChatClient is not implemented in the slice.");

    public Task<string> AnswerAsync(string question, IReadOnlyList<Chunk> context, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FoundryChatClient is not implemented in the slice.");
}
