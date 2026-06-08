using System.Net.Http.Headers;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LandDoc.Tests;

/// <summary>Shared helpers for the ingestion integration tests.</summary>
internal static class IngestionTestHelpers
{
    /// <summary>Posts the synthetic lease fixture to <c>/documents</c> as multipart/form-data.</summary>
    public static async Task<HttpResponseMessage> PostFixtureAsync(HttpClient client)
    {
        using var form = await BuildPdfFormAsync();
        return await client.PostAsync("/documents", form);
    }

    /// <summary>Builds a multipart form with the PDF fixture file.</summary>
    public static async Task<MultipartFormDataContent> BuildPdfFormAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var bytes = await File.ReadAllBytesAsync(path);

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "synthetic-lease-01.pdf");
        return form;
    }

    /// <summary>
    /// Returns every chunk currently in the shared store, read through the public seam (a TopK with a
    /// huge k returns all chunks). The probe vector just needs the store's dimension, so it's produced
    /// by the real embedder.
    /// </summary>
    public static async Task<IReadOnlyList<Chunk>> StoredChunksAsync(WebApplicationFactory<Program> factory)
    {
        var store = factory.Services.GetRequiredService<IVectorStore>();
        var embedder = factory.Services.GetRequiredService<IEmbeddingClient>();
        var probe = await embedder.EmbedAsync("probe");
        var scored = await store.TopKAsync(probe, int.MaxValue);
        return scored.Select(s => s.Chunk).ToList();
    }

    /// <summary>Returns every document currently in the shared document store (spec 0006).</summary>
    public static async Task<IReadOnlyList<DocumentMetadata>> StoredDocumentsAsync(WebApplicationFactory<Program> factory)
    {
        var store = factory.Services.GetRequiredService<IDocumentStore>();
        return await store.ListAsync();
    }
}
