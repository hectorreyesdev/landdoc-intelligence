namespace LandDoc.Api.Storage;

/// <summary>
/// In-memory vector store for the slice (ADR-0005): chunks held in a process-lifetime list; retrieval
/// is top-k by cosine similarity via a linear scan, with a stable tie-break on <see cref="Chunk.Id"/>
/// so a fixed query yields a fixed ordering. Not persisted — rebuilt by re-ingesting.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<Chunk> _chunks = [];
    private readonly object _gate = new();

    // In-memory work is synchronous (no I/O); the async port (ADR-0017) is satisfied with completed
    // tasks so the network-backed adapter can be truly async without blocking a thread.
    public Task AddAsync(Chunk chunk, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        lock (_gate)
        {
            _chunks.Add(chunk);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScoredChunk>> TopKAsync(float[] query, int k, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0)
        {
            return Task.FromResult<IReadOnlyList<ScoredChunk>>([]);
        }

        Chunk[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _chunks];
        }

        IReadOnlyList<ScoredChunk> result = snapshot
            .Select(chunk => new ScoredChunk(chunk, CosineSimilarity(query, chunk.Vector)))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Chunk.Id)
            .Take(k)
            .ToList();
        return Task.FromResult(result);
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException($"Vector dimension mismatch: {a.Length} vs {b.Length}.");
        }

        double dot = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
