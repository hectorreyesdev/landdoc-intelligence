namespace LandDoc.Api.Storage;

/// <summary>A chunk paired with its cosine similarity score, as returned by <see cref="IVectorStore.TopK"/>.</summary>
public sealed record ScoredChunk(Chunk Chunk, double Score);
