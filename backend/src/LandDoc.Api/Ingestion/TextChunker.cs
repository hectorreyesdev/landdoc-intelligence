using Microsoft.Extensions.Options;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Splits text into fixed-size character windows with a small overlap (spec 0001). A general rule applied
/// uniformly to any text — the same input always chunks the same way, with no per-document special-casing.
/// </summary>
public sealed class TextChunker
{
    private readonly int _maxChars;
    private readonly int _overlap;

    public TextChunker(IOptions<ChunkingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (maxChars, overlap) = (options.Value.MaxChars, options.Value.Overlap);

        if (maxChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), maxChars, "Chunk MaxChars must be positive.");
        }

        if (overlap < 0 || overlap >= maxChars)
        {
            throw new ArgumentOutOfRangeException(nameof(options), overlap, "Chunk Overlap must be >= 0 and < MaxChars.");
        }

        _maxChars = maxChars;
        _overlap = overlap;
    }

    public IReadOnlyList<string> Chunk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return [];
        }

        var step = _maxChars - _overlap;
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(_maxChars, text.Length - start);
            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length)
            {
                break;
            }
        }

        return chunks;
    }
}
