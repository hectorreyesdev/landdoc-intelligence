namespace LandDoc.Api.Storage;

/// <summary>
/// The unit stored in the vector store and the 0001→0002 seam: a stable <see cref="Id"/>, the owning
/// <see cref="DocumentId"/>, the source <see cref="Text"/> it was chunked from (kept so the read path
/// can build citations), and its embedding <see cref="Vector"/>. Dropping <see cref="Text"/> or using
/// unstable ids would silently break spec 0002's citations.
/// </summary>
public sealed record Chunk(Guid Id, Guid DocumentId, string Text, float[] Vector);
