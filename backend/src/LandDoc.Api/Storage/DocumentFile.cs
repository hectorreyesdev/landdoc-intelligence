namespace LandDoc.Api.Storage;

/// <summary>
/// The original uploaded bytes of a document plus the metadata needed to serve them back (ADR-0018,
/// spec 0006): returned by <see cref="IDocumentStore.GetFileAsync"/> and streamed by
/// <c>GET /documents/{id}/file</c> with the recorded <see cref="ContentType"/> so a browser renders it
/// inline.
/// </summary>
public sealed record DocumentFile(byte[] Content, string ContentType, string FileName);
