namespace LandDoc.Api.Ingestion;

/// <summary>Options for <see cref="TextChunker"/>, bound from the <c>Chunking</c> configuration section.</summary>
public sealed class ChunkingOptions
{
    /// <summary>
    /// Maximum characters per chunk. Tuned small for the slice's tiny synthetic fixtures so a short
    /// document still splits into more than one chunk (spec 0001). A general rule, not fixture-specific.
    /// </summary>
    public int MaxChars { get; set; } = 120;

    /// <summary>Characters of overlap between consecutive chunks. Must be ≥ 0 and &lt; <see cref="MaxChars"/>.</summary>
    public int Overlap { get; set; } = 30;
}
