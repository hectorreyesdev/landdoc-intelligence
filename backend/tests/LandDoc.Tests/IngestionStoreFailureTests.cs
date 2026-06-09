using System.Net;
using System.Net.Http.Headers;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// ADR-0018 — document persistence is the *required* step of ingest, but the chunks are written to the
/// vector store first. If <see cref="IDocumentStore.SaveAsync"/> throws, the write path must fail (500)
/// AND compensate: the just-written chunks are rolled back so <c>/ask</c> can't retrieve orphan chunks
/// that have no viewable source document. This is the asymmetric counterpart to the best-effort extraction
/// covered by <see cref="IngestionExtractionResilienceTests"/>.
/// </summary>
public sealed class IngestionStoreFailureTests
{
    [Fact]
    public async Task PostDocuments_WhenDocumentStoreThrows_Returns500_AndLeavesNoOrphanChunks()
    {
        using var factory = new ThrowingDocumentStoreFactory();
        var client = factory.CreateClient();

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var pdfBytes = await File.ReadAllBytesAsync(fixturePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "synthetic-lease-01.pdf");

        var response = await client.PostAsync("/documents", form);

        // A failed required-store write surfaces as a 500 (ProblemDetails), not a silent success.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Compensation removed the chunks: a clean store + one failed ingest must leave zero chunks,
        // otherwise /ask would retrieve orphans with no document to "view source".
        var store = factory.Services.GetRequiredService<IVectorStore>();
        var embedder = factory.Services.GetRequiredService<IEmbeddingClient>();
        var probe = await embedder.EmbedAsync("probe");
        var remaining = await store.TopKAsync(probe, int.MaxValue);
        Assert.Empty(remaining);
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{Program}"/> wired with a document store whose
    /// <see cref="IDocumentStore.SaveAsync"/> always throws, plus the in-memory vector store so the
    /// compensation's <see cref="IVectorStore.DeleteByDocumentAsync"/> is exercised against a real store.
    /// </summary>
    private sealed class ThrowingDocumentStoreFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Small chunks so the fixture produces several — proving they're ALL rolled back, not just one.
            builder.UseSetting("Chunking:MaxChars", "80");
            builder.UseSetting("Chunking:Overlap", "20");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVectorStore>();
                services.AddSingleton<IVectorStore>(new InMemoryVectorStore());
                services.RemoveAll<IDocumentStore>();
                services.AddSingleton<IDocumentStore>(new ThrowingDocumentStore());
            });
        }

        private sealed class ThrowingDocumentStore : IDocumentStore
        {
            public Task SaveAsync(DocumentMetadata metadata, byte[] originalBytes, CancellationToken ct = default)
                => throw new InvalidOperationException("Document store unavailable (simulated).");

            public Task<IReadOnlyList<DocumentMetadata>> ListAsync(CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<DocumentMetadata>>([]);

            public Task<DocumentMetadata?> GetAsync(Guid id, CancellationToken ct = default)
                => Task.FromResult<DocumentMetadata?>(null);

            public Task<DocumentFile?> GetFileAsync(Guid id, CancellationToken ct = default)
                => Task.FromResult<DocumentFile?>(null);

            public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
