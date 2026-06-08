using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 amendment — field extraction is best-effort. When the <see cref="IChatClient"/> provider
/// throws (e.g. a missing <c>ModelClient:ApiKey</c> or an unreachable gateway) ingest must NOT surface a
/// 500: it stores the document's chunks and returns 201 with an empty <c>fields</c> array. This proves the
/// extraction step is decoupled from the chunk→embed→store path so every provider — or none — keeps the
/// write path green.
/// </summary>
public sealed class IngestionExtractionResilienceTests
{
    [Fact]
    public async Task PostDocuments_WhenExtractionProviderThrows_Returns201_WithEmptyFields_AndStoresChunks()
    {
        using var factory = new ThrowingExtractionFactory();
        var client = factory.CreateClient();

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var pdfBytes = await File.ReadAllBytesAsync(fixturePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "synthetic-lease-01.pdf");

        var response = await client.PostAsync("/documents", form);

        // A failing extraction provider must degrade to best-effort, not surface as a 500.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);
        Assert.Equal("ready", body!.Status);
        Assert.Empty(body.Fields);                              // extraction threw → no fields
        Assert.True(body.ChunkCount > 1, "Chunks must still be produced when extraction fails.");

        // The chunks really landed in the shared store despite the extraction failure.
        var store = factory.Services.GetRequiredService<IVectorStore>();
        var embedder = factory.Services.GetRequiredService<IEmbeddingClient>();
        var probe = await embedder.EmbedAsync("probe");
        var storedForDoc = store.TopK(probe, int.MaxValue)
            .Where(scored => scored.Chunk.DocumentId == body.Id)
            .ToList();
        Assert.Equal(body.ChunkCount, storedForDoc.Count);
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{Program}"/> wired with a chat client whose
    /// <see cref="IChatClient.ExtractFieldsAsync"/> always throws — simulating an unavailable provider.
    /// Cannot extend the sealed <see cref="LandDocApiFactory"/>, so the override is inlined here.
    /// </summary>
    private sealed class ThrowingExtractionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new ThrowingChatClient());
            });
        }

        private sealed class ThrowingChatClient : IChatClient
        {
            public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Chat provider unavailable (simulated): no ModelClient:ApiKey.");

            public Task<string> AnswerAsync(string question, IReadOnlyList<QaPassage> context, CancellationToken cancellationToken = default)
                => throw new NotSupportedException("AnswerAsync is not exercised by the ingest write path.");
        }
    }
}
