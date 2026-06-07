namespace LandDoc.Api.Qa;

/// <summary>
/// A citation in an <c>/ask</c> response (spec 0002): the source <see cref="ChunkId"/>, its owning
/// <see cref="DocumentId"/>, the cosine <see cref="Score"/>, and the chunk <see cref="Text"/> resolved
/// from the store. Every answered response carries at least one.
/// </summary>
public sealed record Citation(Guid ChunkId, Guid DocumentId, double Score, string Text);
