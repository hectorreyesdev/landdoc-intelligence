namespace LandDoc.Api.Storage;

/// <summary>
/// Narrow store seam (ADR-0005): ingestion writes chunks via <see cref="AddAsync"/>, retrieval reads
/// the top-k most similar via <see cref="TopKAsync"/>. Registered as a singleton so the write and read
/// paths share one instance. The port is **async** (ADR-0017) because the live adapter
/// (<see cref="AzureAiSearchVectorStore"/>) does network I/O; the in-memory adapter satisfies it with
/// completed tasks. Config-selected via <c>VectorStore:Provider</c>.
/// </summary>
public interface IVectorStore
{
    /// <summary>Adds a chunk (and its vector) to the store.</summary>
    Task AddAsync(Chunk chunk, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="k"/> chunks most similar to <paramref name="query"/> by cosine
    /// similarity, highest first, with a deterministic tie-break.
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> TopKAsync(float[] query, int k, CancellationToken ct = default);

    /// <summary>Removes every chunk belonging to <paramref name="documentId"/> (spec 0008). A no-op when none match.</summary>
    Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default);
}
