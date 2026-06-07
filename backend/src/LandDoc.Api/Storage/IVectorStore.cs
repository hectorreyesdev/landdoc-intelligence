namespace LandDoc.Api.Storage;

/// <summary>
/// Narrow store seam (ADR-0005): ingestion writes chunks via <see cref="Add"/>, retrieval reads the
/// top-k most similar via <see cref="TopK"/>. Registered as a singleton so the write and read paths
/// share one in-memory instance. Mirrors the port pattern so the production swap to Azure AI Search is
/// an adapter change, not a rewrite.
/// </summary>
public interface IVectorStore
{
    /// <summary>Adds a chunk (and its vector) to the store.</summary>
    void Add(Chunk chunk);

    /// <summary>
    /// Returns up to <paramref name="k"/> chunks most similar to <paramref name="query"/> by cosine
    /// similarity, highest first, with a deterministic tie-break.
    /// </summary>
    IReadOnlyList<ScoredChunk> TopK(float[] query, int k);
}
