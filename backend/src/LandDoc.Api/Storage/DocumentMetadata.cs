using LandDoc.Api.Model;

namespace LandDoc.Api.Storage;

/// <summary>
/// The persisted document-level record (ADR-0018, spec 0006): everything about an ingested document
/// except its original bytes. Stored by <see cref="IDocumentStore"/> at ingest and returned by
/// <c>GET /documents</c> (list) and <c>GET /documents/{id}</c> (detail). <see cref="ChunkCount"/> is the
/// number of chunks the document produced (known at ingest); <see cref="Fields"/> are the best-effort
/// extracted fields (may be empty). Unlike the chunk store (<see cref="IVectorStore"/>), this layer keeps
/// document-grained metadata so the UI can list documents and label citations.
/// </summary>
public sealed record DocumentMetadata(
    Guid Id,
    string FileName,
    string Status,
    string ContentType,
    int ChunkCount,
    IReadOnlyList<ExtractedField> Fields,
    DateTimeOffset IngestedAt);
