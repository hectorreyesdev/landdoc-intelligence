using System.Net;
using System.Net.Http.Json;
using System.Text;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0005: POST /documents accepts .txt, .md, and .markdown uploads. Text files are UTF-8-decoded
/// (no PDF parse) and flow through the existing chunk → embed → store + best-effort extraction path
/// unchanged. Any other extension returns 400.
/// </summary>
public sealed class TextIngestionTests(LandDocApiFactory factory) : IClassFixture<LandDocApiFactory>
{
    // --- happy path: response contract ---

    [Theory]
    [InlineData("synthetic-lease-01.md")]
    [InlineData("synthetic-lease-01.txt")]
    public async Task PostDocuments_TextFile_Returns201_WithFullContract(string fileName)
    {
        var client = factory.CreateClient();
        using var form = await BuildFormFromFixtureAsync(fileName);

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.Equal(fileName, body.FileName);
        Assert.Equal("ready", body.Status);
        Assert.True(body.ChunkCount >= 1, "Expected at least one chunk.");
    }

    // --- extraction over text: fake IChatClient fields flow through unchanged ---

    [Theory]
    [InlineData("synthetic-lease-01.md")]
    [InlineData("synthetic-lease-01.txt")]
    public async Task PostDocuments_TextFile_ReturnsExactFieldsFromChatClient(string fileName)
    {
        var client = factory.CreateClient();
        using var form = await BuildFormFromFixtureAsync(fileName);

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);

        // These are exactly what FakeChatClient returns; they must flow through unchanged.
        Assert.Equal(5, body!.Fields.Count);
        Assert.Contains(body.Fields, f => f is { Name: "Lessor", Value: "John Q. Landowner" });
        Assert.Contains(body.Fields, f => f is { Name: "Lessee", Value: "Acme Minerals LLC" });
        Assert.Contains(body.Fields, f => f is { Name: "Royalty", Value: "3/16" });
    }

    // --- storage invariants: chunk shape and 0001→0002 seam ---

    [Theory]
    [InlineData("synthetic-lease-01.md")]
    [InlineData("synthetic-lease-01.txt")]
    public async Task PostDocuments_TextFile_StoresChunksWithCorrectShape(string fileName)
    {
        using var isolatedFactory = new LandDocApiFactory();
        var client = isolatedFactory.CreateClient();
        using var form = await BuildFormFromFixtureAsync(fileName);

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);

        var stored = await IngestionTestHelpers.StoredChunksAsync(isolatedFactory);
        var forDocument = stored.Where(c => c.DocumentId == body!.Id).ToList();

        Assert.Equal(body!.ChunkCount, forDocument.Count);
        Assert.NotEmpty(forDocument);

        // All vectors non-empty and equal length (0001→0002 seam invariant).
        Assert.All(forDocument, chunk => Assert.NotEmpty(chunk.Vector));
        var dimension = forDocument[0].Vector.Length;
        Assert.All(forDocument, chunk => Assert.Equal(dimension, chunk.Vector.Length));

        // Each chunk has a stable, unique Id and correct DocumentId.
        Assert.All(forDocument, chunk =>
        {
            Assert.NotEqual(Guid.Empty, chunk.Id);
            Assert.Equal(body.Id, chunk.DocumentId);
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
        });
        Assert.Equal(forDocument.Count, forDocument.Select(c => c.Id).Distinct().Count());
    }

    // --- text decoded, not PDF-parsed: non-PDF bytes under a text extension succeed ---

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public async Task PostDocuments_TextExtensionWithNonPdfBytes_Returns201(string extension)
    {
        using var isolatedFactory = new LandDocApiFactory();
        var client = isolatedFactory.CreateClient();

        // Bytes are valid UTF-8 text but not a PDF — would fail magic-byte guard if mistakenly routed there.
        var bytes = Encoding.UTF8.GetBytes("This is plain UTF-8 text. It is definitely not a PDF document.");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", $"document{extension}");

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // --- unsupported extension → 400, nothing stored ---

    [Theory]
    [InlineData(".png")]
    [InlineData(".docx")]
    [InlineData(".html")]
    [InlineData("")]
    public async Task PostDocuments_UnsupportedExtension_Returns400_NothingStored(string extension)
    {
        using var isolatedFactory = new LandDocApiFactory();
        var client = isolatedFactory.CreateClient();

        var bytes = Encoding.UTF8.GetBytes("some content");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", $"document{extension}");

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await IngestionTestHelpers.StoredChunksAsync(isolatedFactory));
    }

    // --- PDF regression: existing 0001 PDF happy path still works ---

    [Fact]
    public async Task PostDocuments_PdfFile_StillReturns201_WithChunks()
    {
        // Use an isolated factory that pins small chunk size so the fixture yields > 1 chunk.
        using var smallFactory = new SmallChunkFactory();
        var client = smallFactory.CreateClient();
        using var form = await IngestionTestHelpers.BuildPdfFormAsync();

        var response = await client.PostAsync("/documents", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);
        Assert.True(body!.ChunkCount > 1, "PDF fixture must still produce more than one chunk.");
    }

    // --- helpers ---

    /// <summary>Pins small chunk size so fixtures yield > 1 chunk regardless of the production default.</summary>
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

    private static async Task<MultipartFormDataContent> BuildFormFromFixtureAsync(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        var bytes = await File.ReadAllBytesAsync(path);
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }
}
