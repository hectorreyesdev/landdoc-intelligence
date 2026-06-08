namespace LandDoc.Api.Model;

/// <summary>
/// Embeddings port (ADR-0013 supersedes ADR-0008). Live slice default <c>AzureOpenAIEmbeddingClient</c>
/// (<c>text-embedding-3-small</c>); <c>LocalEmbeddingClient</c> is the deterministic offline fallback.
/// Provider is config-selected via <c>ModelClient:EmbeddingProvider</c> (<c>azureopenai</c> or
/// <c>local</c>). There is no Anthropic embeddings adapter. Changing this interface requires a spec.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>Embeds text into a fixed-dimension vector. Same text → same vector (ADR-0008).</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
