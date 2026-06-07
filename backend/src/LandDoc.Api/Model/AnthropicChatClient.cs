using LandDoc.Api.Storage;

namespace LandDoc.Api.Model;

/// <summary>
/// Anthropic API direct chat adapter — the fallback provider (ADR-0002/0007). A slice stub: the
/// acceptance tests inject a fake <see cref="IChatClient"/> and both endpoints return 501, so this is
/// never called. The real Anthropic SDK wiring lands with a later spec.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("AnthropicChatClient is not implemented in the slice.");

    public Task<string> AnswerAsync(string question, IReadOnlyList<Chunk> context, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("AnthropicChatClient is not implemented in the slice.");
}
