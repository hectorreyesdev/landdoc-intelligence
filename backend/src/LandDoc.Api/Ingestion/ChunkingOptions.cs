namespace LandDoc.Api.Ingestion;

/// <summary>Options for <see cref="TextChunker"/>, bound from the <c>Chunking</c> configuration section.</summary>
public sealed class ChunkingOptions
{
    /// <summary>
    /// Maximum characters per chunk. Keeps a clause or section intact so retrieval doesn't fragment
    /// critical facts across chunk boundaries. Tests that assert multiple chunks must pin their own
    /// <c>Chunking:MaxChars</c> via the test host rather than relying on this default.
    /// </summary>
    public int MaxChars { get; set; } = 800;

    /// <summary>Characters of overlap between consecutive chunks. Must be ≥ 0 and &lt; <see cref="MaxChars"/>.</summary>
    public int Overlap { get; set; } = 150;
}
