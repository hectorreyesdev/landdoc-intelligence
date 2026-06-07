namespace LandDoc.Api.Retrieval;

/// <summary>Config options for the retrieval step — bound from the <c>Retrieval</c> config section.</summary>
public sealed class RetrievalOptions
{
    /// <summary>Number of top-k chunks to retrieve per query (default 5). Configurable via <c>Retrieval:TopK</c>.</summary>
    public int TopK { get; init; } = 5;
}
