using System.Net;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 storage-side verification: after ingest, the shared store holds exactly the reported number
/// of chunks for the document, every vector is non-empty and the same length, and each chunk retains the
/// full { Id, DocumentId, Text, Vector } shape (the 0001→0002 seam). Each test gets its own factory, so
/// its store is isolated.
/// </summary>
public sealed class IngestionStorageTests
{
    [Fact]
    public async Task Ingest_StoresExactlyChunkCountChunks_AllSameLengthNonEmptyVectors()
    {
        using var factory = new SmallChunkFactory();
        var response = await IngestionTestHelpers.PostFixtureAsync(factory.CreateClient());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);

        var stored = await IngestionTestHelpers.StoredChunksAsync(factory);
        var forDocument = stored.Where(chunk => chunk.DocumentId == body!.Id).ToList();

        Assert.Equal(body!.ChunkCount, forDocument.Count);
        Assert.True(forDocument.Count > 1, "Expected the document to produce more than one chunk.");
        Assert.All(forDocument, chunk => Assert.NotEmpty(chunk.Vector));

        var dimension = forDocument[0].Vector.Length;
        Assert.All(forDocument, chunk => Assert.Equal(dimension, chunk.Vector.Length));
    }

    [Fact]
    public async Task Ingest_StoredChunksRetainText_StableIds_AndCorrectDocumentId()
    {
        using var factory = new LandDocApiFactory();
        var response = await IngestionTestHelpers.PostFixtureAsync(factory.CreateClient());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);

        var stored = (await IngestionTestHelpers.StoredChunksAsync(factory))
            .Where(chunk => chunk.DocumentId == body!.Id)
            .ToList();

        Assert.NotEmpty(stored);
        Assert.All(stored, chunk =>
        {
            Assert.NotEqual(Guid.Empty, chunk.Id);
            Assert.Equal(body!.Id, chunk.DocumentId);
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
        });
        Assert.Equal(stored.Count, stored.Select(chunk => chunk.Id).Distinct().Count());
    }

    /// <summary>Pins small chunk size so the fixture yields > 1 chunk regardless of the production default.</summary>
    private sealed class SmallChunkFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Chunking:MaxChars", "80");
            builder.UseSetting("Chunking:Overlap", "20");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient, FakeChatClient>();
            });
        }
    }
}
