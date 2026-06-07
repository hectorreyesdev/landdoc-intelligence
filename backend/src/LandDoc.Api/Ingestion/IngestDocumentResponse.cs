using LandDoc.Api.Model;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Response body for <c>POST /documents</c> (spec 0001): the new document id, the upload's file name,
/// status, the extracted fields, and the number of chunks embedded and stored.
/// </summary>
public sealed record IngestDocumentResponse(
    Guid Id,
    string FileName,
    string Status,
    IReadOnlyList<ExtractedField> Fields,
    int ChunkCount);
