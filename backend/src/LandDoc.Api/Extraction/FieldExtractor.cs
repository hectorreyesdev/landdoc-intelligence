using LandDoc.Api.Model;

namespace LandDoc.Api.Extraction;

/// <summary>
/// The Extraction module's LLM call (spec 0001): turns document text into the document's key structured
/// fields by delegating to the <see cref="IChatClient"/> port. For a real provider the prompt/parse lives
/// in the chat adapter; in tests the fake returns canned fields. This type just owns the port call and
/// guarantees a non-null result.
/// </summary>
public sealed class FieldExtractor(IChatClient chatClient)
{
    public async Task<IReadOnlyList<ExtractedField>> ExtractAsync(string documentText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        var fields = await chatClient.ExtractFieldsAsync(documentText, cancellationToken);
        return fields ?? [];
    }
}
