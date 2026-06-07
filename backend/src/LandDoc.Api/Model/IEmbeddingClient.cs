namespace LandDoc.Api.Model;

/// <summary>
/// Embeddings port (ADR-0002). Slice default <c>LocalEmbeddingClient</c> (deterministic, offline);
/// <c>FoundryEmbeddingClient</c> (Azure OpenAI) is the production path and out of scope. There is no
/// Anthropic embeddings adapter. Changing this interface requires a spec.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>Embeds text into a fixed-dimension vector. Same text → same vector (ADR-0008).</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
