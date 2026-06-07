using System.Text;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Model;

/// <summary>
/// Deterministic hashing / bag-of-words embedder (ADR-0008): tokenizes text, hashes each token (FNV-1a)
/// into a fixed-dimension term-count vector, then L2-normalizes — so the same text always yields the
/// same vector, fully offline with no model file. Uses a stable hash on purpose: <c>string.GetHashCode</c>
/// is randomized per process and would make embeddings non-reproducible across runs.
/// </summary>
public sealed class LocalEmbeddingClient : IEmbeddingClient
{
    private readonly int _dimension;

    public LocalEmbeddingClient(IOptions<EmbeddingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var dimension = options.Value.Dimension;
        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), dimension, "Embedding dimension must be positive.");
        }

        _dimension = dimension;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vector = new float[_dimension];
        foreach (var token in Tokenize(text))
        {
            var bucket = (int)(Fnv1a(token) % (uint)_dimension);
            vector[bucket] += 1f;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var token = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                token.Append(char.ToLowerInvariant(ch));
            }
            else if (token.Length > 0)
            {
                yield return token.ToString();
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    // FNV-1a (32-bit): a stable, process-independent hash so embeddings are reproducible across runs.
    private static uint Fnv1a(string token)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(token))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        double sumOfSquares = 0;
        foreach (var value in vector)
        {
            sumOfSquares += value * (double)value;
        }

        if (sumOfSquares == 0)
        {
            return;
        }

        var magnitude = (float)Math.Sqrt(sumOfSquares);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }
    }
}
