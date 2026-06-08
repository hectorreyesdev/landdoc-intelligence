namespace LandDoc.Api.Storage;

/// <summary>
/// Persisted document store (ADR-0018): ingestion saves the original bytes + metadata via
/// <see cref="SaveAsync"/>; the read endpoints list/fetch via <see cref="ListAsync"/>,
/// <see cref="GetAsync"/>, and <see cref="GetFileAsync"/>. A sibling to <see cref="IVectorStore"/> — the
/// vector store is a chunk-grained similarity index, this is document-grained object storage (byte
/// payloads + metadata). Registered as a singleton so the write and read paths share one instance. The
/// port is async because the live adapter (<see cref="AzureBlobDocumentStore"/>) does network I/O; the
/// in-memory adapter satisfies it with completed tasks. Config-selected via <c>DocumentStore:Provider</c>.
/// </summary>
public interface IDocumentStore
{
    /// <summary>Persists a document's metadata and its original uploaded bytes.</summary>
    Task SaveAsync(DocumentMetadata metadata, byte[] originalBytes, CancellationToken ct = default);

    /// <summary>Lists the metadata of every stored document (empty when none have been ingested).</summary>
    Task<IReadOnlyList<DocumentMetadata>> ListAsync(CancellationToken ct = default);

    /// <summary>Returns one document's metadata, or <c>null</c> if no document has that id (→ 404).</summary>
    Task<DocumentMetadata?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns one document's original bytes + content type, or <c>null</c> if unknown (→ 404).</summary>
    Task<DocumentFile?> GetFileAsync(Guid id, CancellationToken ct = default);
}
