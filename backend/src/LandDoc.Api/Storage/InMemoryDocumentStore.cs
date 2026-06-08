namespace LandDoc.Api.Storage;

/// <summary>
/// In-memory document store for the offline/test path (ADR-0018): metadata + original bytes held in a
/// process-lifetime dictionary keyed by document id. Not persisted — lost on restart. Pinned in tests via
/// <c>DocumentStore:Provider=inmemory</c> so the suite runs with no storage credentials, mirroring
/// <see cref="InMemoryVectorStore"/>.
/// </summary>
public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly Dictionary<Guid, (DocumentMetadata Meta, byte[] Bytes)> _documents = [];
    private readonly object _gate = new();

    // In-memory work is synchronous (no I/O); the async port (ADR-0018) is satisfied with completed
    // tasks so the network-backed Blob adapter can be truly async without blocking a thread.
    public Task SaveAsync(DocumentMetadata metadata, byte[] originalBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(originalBytes);
        lock (_gate)
        {
            _documents[metadata.Id] = (metadata, originalBytes);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentMetadata>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<DocumentMetadata> all = _documents.Values
                .Select(entry => entry.Meta)
                .OrderBy(meta => meta.IngestedAt)
                .ThenBy(meta => meta.Id)
                .ToList();
            return Task.FromResult(all);
        }
    }

    public Task<DocumentMetadata?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(id, out var entry) ? entry.Meta : null);
        }
    }

    public Task<DocumentFile?> GetFileAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var file = _documents.TryGetValue(id, out var entry)
                ? new DocumentFile(entry.Bytes, entry.Meta.ContentType, entry.Meta.FileName)
                : null;
            return Task.FromResult(file);
        }
    }
}
