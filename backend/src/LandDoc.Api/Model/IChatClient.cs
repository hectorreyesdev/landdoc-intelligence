using LandDoc.Api.Storage;

namespace LandDoc.Api.Model;

/// <summary>
/// Chat/completions port (ADR-0002). Adapters select by config (<c>ModelClient:ChatProvider</c>):
/// <c>FoundryChatClient</c> (primary) and <c>AnthropicChatClient</c> (fallback). Changing this
/// interface requires a spec in <c>knowledge/docs/specs/</c>.
/// </summary>
public interface IChatClient
{
    /// <summary>Extracts the document's key structured fields — the Extraction module's LLM call (spec 0001).</summary>
    Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default);

    /// <summary>Composes an answer grounded only in the supplied chunks — the Qa module's LLM call (spec 0002).</summary>
    Task<string> AnswerAsync(string question, IReadOnlyList<Chunk> context, CancellationToken cancellationToken = default);
}
