using System.Net.Http.Headers;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LandDoc.Tests;

/// <summary>Shared helpers for the ingestion integration tests.</summary>
internal static class IngestionTestHelpers
{
    /// <summary>Posts the synthetic lease fixture to <c>/documents</c> as multipart/form-data.</summary>
    public static async Task<HttpResponseMessage> PostFixtureAsync(HttpClient client)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var bytes = await File.ReadAllBytesAsync(path);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "synthetic-lease-01.pdf");

        return await client.PostAsync("/documents", form);
    }

    /// <summary>
    /// Returns every chunk currently in the shared store, read through the public seam (a TopK with a
    /// huge k returns all chunks). The probe vector just needs the store's dimension, so it's produced
    /// by the real embedder.
    /// </summary>
    public static async Task<IReadOnlyList<Chunk>> StoredChunksAsync(LandDocApiFactory factory)
    {
        var store = factory.Services.GetRequiredService<IVectorStore>();
        var embedder = factory.Services.GetRequiredService<IEmbeddingClient>();
        var probe = await embedder.EmbedAsync("probe");
        return store.TopK(probe, int.MaxValue).Select(scored => scored.Chunk).ToList();
    }
}
