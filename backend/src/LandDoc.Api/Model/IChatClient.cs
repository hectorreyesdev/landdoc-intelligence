namespace LandDoc.Api.Model;

/// <summary>
/// Chat/completions port (ADR-0002). Adapters select by config (<c>ModelClient:ChatProvider</c>):
/// <c>FoundryChatClient</c> (primary) and <c>AnthropicChatClient</c> (slice default, ADR-0010).
/// Changing this interface requires a spec in <c>knowledge/docs/specs/</c>.
/// </summary>
public interface IChatClient
{
    /// <summary>Extracts the document's key structured fields — the Extraction module's LLM call (spec 0001).</summary>
    Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Composes an answer grounded only in the supplied passages — the Qa module's LLM call (spec 0002).
    /// Takes <see cref="QaPassage"/> port DTOs (not storage <c>Chunk</c>) so this port carries no
    /// dependency on the <c>Storage</c> namespace (hexagonal ports, ADR-0002/ADR-0004).
    /// </summary>
    Task<string> AnswerAsync(string question, IReadOnlyList<QaPassage> context, CancellationToken cancellationToken = default);
}
