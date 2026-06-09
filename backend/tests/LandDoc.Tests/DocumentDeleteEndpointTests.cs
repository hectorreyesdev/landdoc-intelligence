using System.Net;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0008 — DELETE /documents/{id} removes the document from BOTH stores (file + metadata and all its
/// chunks) and is idempotent. Each test owns its factory so the in-memory stores are isolated.
/// </summary>
public sealed class DocumentDeleteEndpointTests
{
    [Fact]
    public async Task Delete_removesDocumentFromListFileAndVectorStore()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingest = await IngestionTestHelpers.PostFixtureAsync(client);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var delete = await client.DeleteAsync($"/documents/{ingested!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Gone from the document store: list omits it, file is 404.
        Assert.Empty(await IngestionTestHelpers.StoredDocumentsAsync(factory));
        var fileResponse = await client.GetAsync($"/documents/{ingested.Id}/file");
        Assert.Equal(HttpStatusCode.NotFound, fileResponse.StatusCode);

        // Gone from the vector store: its chunks are removed, so the (now empty) corpus answers 409.
        Assert.Empty(await IngestionTestHelpers.StoredChunksAsync(factory));
        var ask = await client.PostAsJsonAsync("/ask", new { question = "Who is the lessee?" });
        Assert.Equal(HttpStatusCode.Conflict, ask.StatusCode);
    }

    [Fact]
    public async Task Delete_unknownId_returns204()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var delete = await client.DeleteAsync($"/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_oneOfTwo_leavesTheOther()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var first = await (await IngestionTestHelpers.PostFixtureAsync(client)).Content.ReadFromJsonAsync<IngestDocumentResponse>();
        var second = await (await IngestionTestHelpers.PostFixtureAsync(client)).Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var delete = await client.DeleteAsync($"/documents/{first!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var remaining = await IngestionTestHelpers.StoredDocumentsAsync(factory);
        Assert.Single(remaining);
        Assert.Equal(second!.Id, remaining[0].Id);
    }
}
