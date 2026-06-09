namespace LandDoc.Api.Qa;

/// <summary>
/// A citation in an <c>/ask</c> response (spec 0002, amended by spec 0006): the source
/// <see cref="ChunkId"/>, its owning <see cref="DocumentId"/>, the cosine <see cref="Score"/>, the chunk
/// <see cref="Text"/> resolved from the store, and the <see cref="Source"/> file name (ADR-0014 follow-on)
/// so the UI can label the citation and link to <c>GET /documents/{DocumentId}</c>. Every answered response
/// carries at least one.
/// </summary>
public sealed record Citation(Guid ChunkId, Guid DocumentId, double Score, string Text, string Source);
