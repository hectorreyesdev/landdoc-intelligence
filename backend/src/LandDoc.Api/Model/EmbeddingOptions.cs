namespace LandDoc.Api.Model;

/// <summary>Options for the local embedder, bound from the <c>Embedding</c> configuration section.</summary>
public sealed class EmbeddingOptions
{
    /// <summary>Fixed vector dimension shared by all embeddings (the cosine invariant). Default 256 (ADR-0008).</summary>
    public int Dimension { get; set; } = 256;
}
