using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Retrieval;

/// <summary>
/// Retrieval seam (spec 0004): embeds the question via <see cref="IEmbeddingClient"/> then returns the
/// top-k most similar chunks from the store. Owned by the <c>Retrieval</c> module; the <c>Qa</c> handler
/// calls this and maps results to citations without knowing how retrieval works internally.
/// </summary>
public sealed class ChunkRetriever
{
    private readonly IEmbeddingClient _embedder;
    private readonly IVectorStore _store;
    private readonly RetrievalOptions _options;

    public ChunkRetriever(IEmbeddingClient embedder, IVectorStore store, IOptions<RetrievalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        _embedder = embedder;
        _store = store;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var queryVector = await _embedder.EmbedAsync(question, ct);
        return _store.TopK(queryVector, _options.TopK);
    }
}
