using LandDoc.Api.Model;
using Microsoft.Extensions.Options;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 / ADR-0008: <c>LocalEmbeddingClient</c> is deterministic — the same text always yields the
/// same fixed-dimension vector — and content-sensitive, so different text yields a different vector
/// (guards against a degenerate or hardcoded embedder).
/// </summary>
public sealed class LocalEmbeddingClientTests
{
    private static LocalEmbeddingClient CreateClient(int dimension = 256) =>
        new(Options.Create(new EmbeddingOptions { Dimension = dimension }));

    [Fact]
    public async Task EmbedAsync_SameText_ProducesIdenticalVectors()
    {
        var client = CreateClient();
        const string text = "by and between John Q. Landowner as Lessor and Acme Minerals LLC as Lessee";

        var first = await client.EmbedAsync(text);
        var second = await client.EmbedAsync(text);

        Assert.Equal(first, second);
        Assert.Equal(256, first.Length);
    }

    [Fact]
    public async Task EmbedAsync_DifferentText_ProducesDifferentVectors()
    {
        var client = CreateClient();

        var lessee = await client.EmbedAsync("the lessee is Acme Minerals LLC");
        var royalty = await client.EmbedAsync("the royalty reserved is three sixteenths");

        Assert.NotEqual(lessee, royalty);
    }
}
