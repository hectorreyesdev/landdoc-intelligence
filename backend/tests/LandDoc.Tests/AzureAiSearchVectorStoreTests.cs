using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

namespace LandDoc.Tests;

/// <summary>
/// ADR-0017 — <see cref="AzureAiSearchVectorStore"/> fails fast with a clear message when required
/// config is missing. The guard runs before any network call (the Azure SDK doesn't hit the network
/// until after a valid client is built and a request is issued), so these tests are fully offline.
/// </summary>
public sealed class AzureAiSearchVectorStoreTests
{
    private static IOptions<SearchOptions> SearchOpts(string? endpoint, string? apiKey) =>
        Options.Create(new SearchOptions { Endpoint = endpoint, ApiKey = apiKey });

    private static IOptions<EmbeddingOptions> EmbedOpts() =>
        Options.Create(new EmbeddingOptions { Dimension = 256 });

    [Fact]
    public void Ctor_MissingEndpoint_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureAiSearchVectorStore(SearchOpts(null, "key"), EmbedOpts()));
        Assert.Contains("Search:Endpoint", ex.Message);
    }

    [Fact]
    public void Ctor_MissingApiKey_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureAiSearchVectorStore(SearchOpts("https://example.search.windows.net", null), EmbedOpts()));
        Assert.Contains("Search:ApiKey", ex.Message);
    }
}
