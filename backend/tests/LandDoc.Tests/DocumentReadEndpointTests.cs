using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0006 — the document read-back surface: <c>GET /documents</c> (list), <c>GET /documents/{id}</c>
/// (detail + 404), and <c>GET /documents/{id}/file</c> (original bytes, content type, 404). Each test owns
/// its own factory so the in-memory document store is isolated.
/// </summary>
public sealed class DocumentReadEndpointTests
{
    [Fact]
    public async Task Ingest_then_GET_documents_listsTheDocument()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingest = await IngestionTestHelpers.PostFixtureAsync(client);
        Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var response = await client.GetAsync("/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var documents = await response.Content.ReadFromJsonAsync<List<DocumentMetadata>>();
        Assert.NotNull(documents);
        var doc = Assert.Single(documents!);
        Assert.Equal(ingested!.Id, doc.Id);
        Assert.Equal("synthetic-lease-01.pdf", doc.FileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal(ingested.ChunkCount, doc.ChunkCount);
        Assert.NotEmpty(doc.Fields);
    }

    [Fact]
    public async Task GET_documents_emptyStore_returns200_emptyList()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var documents = await response.Content.ReadFromJsonAsync<List<DocumentMetadata>>();
        Assert.NotNull(documents);
        Assert.Empty(documents!);
    }

    [Fact]
    public async Task GET_documents_id_returnsMetadata()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingest = await IngestionTestHelpers.PostFixtureAsync(client);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var response = await client.GetAsync($"/documents/{ingested!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<DocumentMetadata>();
        Assert.NotNull(doc);
        Assert.Equal(ingested.Id, doc!.Id);
        Assert.Equal("synthetic-lease-01.pdf", doc.FileName);
        Assert.Equal(ingested.ChunkCount, doc.ChunkCount);
        Assert.NotEmpty(doc.Fields);
    }

    [Fact]
    public async Task GET_documents_id_unknown_returns404()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GET_documents_id_file_returnsBytes_withPdfContentType()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingest = await IngestionTestHelpers.PostFixtureAsync(client);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var response = await client.GetAsync($"/documents/{ingested!.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var expected = await File.ReadAllBytesAsync(path);
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GET_documents_id_file_text_returnsMarkdownContentType()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        const string markdown = "# Lease\n\nLessee: Acme Minerals LLC\nLessor: John Q. Landowner\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(markdown);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(fileContent, "file", "lease.md");

        var ingest = await client.PostAsync("/documents", form);
        Assert.Equal(HttpStatusCode.Created, ingest.StatusCode);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var response = await client.GetAsync($"/documents/{ingested!.Id}/file");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        var actual = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, actual);
    }

    [Fact]
    public async Task GET_documents_id_file_unknown_returns404()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/documents/{Guid.NewGuid()}/file");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_persists_documentBytes_inStore()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingest = await IngestionTestHelpers.PostFixtureAsync(client);
        var ingested = await ingest.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(ingested);

        var documents = await IngestionTestHelpers.StoredDocumentsAsync(factory);
        var doc = Assert.Single(documents);
        Assert.Equal(ingested!.Id, doc.Id);

        var store = factory.Services.GetRequiredService<IDocumentStore>();
        var file = await store.GetFileAsync(doc.Id);
        Assert.NotNull(file);

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var expected = await File.ReadAllBytesAsync(path);
        Assert.Equal(expected, file!.Content);
    }
}
