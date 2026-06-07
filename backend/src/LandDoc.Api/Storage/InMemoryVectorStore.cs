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

    public void Add(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        lock (_gate)
        {
            _chunks.Add(chunk);
        }
    }

    public IReadOnlyList<ScoredChunk> TopK(float[] query, int k)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0)
        {
            return [];
        }

        Chunk[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _chunks];
        }

        return snapshot
            .Select(chunk => new ScoredChunk(chunk, CosineSimilarity(query, chunk.Vector)))
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Chunk.Id)
            .Take(k)
            .ToList();
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
