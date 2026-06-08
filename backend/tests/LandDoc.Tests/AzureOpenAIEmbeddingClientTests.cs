using LandDoc.Api.Model;
using Microsoft.Extensions.Options;

namespace LandDoc.Tests;

/// <summary>
/// ADR-0013 / spec 0001+0002 amendments — <see cref="AzureOpenAIEmbeddingClient"/> builds its underlying
/// SDK client ONCE in the ctor and fails fast with a clear message when required config is missing. No
/// live-service call: construction is offline (the Azure SDK doesn't hit the network until a request is
/// made), so these run in the deterministic unit suite.
/// </summary>
public sealed class AzureOpenAIEmbeddingClientTests
{
    private const string Endpoint = "https://example-resource.openai.azure.com/";

    private static IOptions<AzureOpenAIOptions> AzureOpts(
        string? endpoint, string? apiKey, string? embeddingDeployment) =>
        Options.Create(new AzureOpenAIOptions
        {
            Endpoint = endpoint,
            ApiKey = apiKey,
            EmbeddingDeployment = embeddingDeployment,
        });

    private static IOptions<EmbeddingOptions> EmbedOpts(int dimension = 256) =>
        Options.Create(new EmbeddingOptions { Dimension = dimension });

    [Fact]
    public void Ctor_MissingEndpoint_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIEmbeddingClient(
                AzureOpts(null, "key", "text-embedding-3-small"), EmbedOpts()));
        Assert.Contains("AzureOpenAI:Endpoint", ex.Message);
    }

    [Fact]
    public void Ctor_MissingApiKey_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIEmbeddingClient(
                AzureOpts(Endpoint, null, "text-embedding-3-small"), EmbedOpts()));
        Assert.Contains("AzureOpenAI:ApiKey", ex.Message);
    }

    [Fact]
    public void Ctor_MissingEmbeddingDeployment_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIEmbeddingClient(
                AzureOpts(Endpoint, "key", null), EmbedOpts()));
        Assert.Contains("AzureOpenAI:EmbeddingDeployment", ex.Message);
    }

    [Fact]
    public void Ctor_AllConfigPresent_ConstructsWithoutThrowing()
    {
        var client = new AzureOpenAIEmbeddingClient(
            AzureOpts(Endpoint, "key", "text-embedding-3-small"), EmbedOpts());
        Assert.NotNull(client);
    }
}
