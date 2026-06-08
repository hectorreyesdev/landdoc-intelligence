using LandDoc.Api.Model;
using Microsoft.Extensions.Options;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0002 / ADR-0012 — <see cref="AzureOpenAIChatClient"/> builds its client once in the ctor and
/// fails fast with a clear message when required config is missing. No live-service call: construction
/// is offline (the Azure SDK doesn't hit the network until a request is made), so these run in the
/// deterministic unit suite.
/// </summary>
public sealed class AzureOpenAIChatClientTests
{
    private const string Endpoint = "https://example-resource.openai.azure.com/";

    private static IOptions<AzureOpenAIOptions> Opts(string? endpoint, string? apiKey, string? deployment) =>
        Options.Create(new AzureOpenAIOptions { Endpoint = endpoint, ApiKey = apiKey, Deployment = deployment });

    [Fact]
    public void Ctor_MissingEndpoint_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIChatClient(Opts(null, "key", "gpt-5.4-mini")));
        Assert.Contains("AzureOpenAI:Endpoint", ex.Message);
    }

    [Fact]
    public void Ctor_MissingApiKey_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIChatClient(Opts(Endpoint, null, "gpt-5.4-mini")));
        Assert.Contains("AzureOpenAI:ApiKey", ex.Message);
    }

    [Fact]
    public void Ctor_MissingDeployment_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIChatClient(Opts(Endpoint, "key", null)));
        Assert.Contains("AzureOpenAI:Deployment", ex.Message);
    }

    [Fact]
    public void Ctor_AllConfigPresent_ConstructsWithoutThrowing()
    {
        var client = new AzureOpenAIChatClient(Opts(Endpoint, "key", "gpt-5.4-mini"));
        Assert.NotNull(client);
    }
}
