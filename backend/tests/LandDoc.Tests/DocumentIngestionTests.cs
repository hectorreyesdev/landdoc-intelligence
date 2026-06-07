using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 happy path. RED until <c>POST /documents</c> is implemented: the endpoint returns 501, so
/// the 201 assertion below fails (the intended reason). The contract assertions after it are written now
/// so the test turns green once the ingest pipeline lands — no test rewrite needed.
/// </summary>
public sealed class DocumentIngestionTests(LandDocApiFactory factory) : IClassFixture<LandDocApiFactory>
{
    [Fact]
    public async Task PostDocuments_HappyPath_Returns201_WithFullContract()
    {
        var client = factory.CreateClient();

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-lease-01.pdf");
        var pdfBytes = await File.ReadAllBytesAsync(fixturePath);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "synthetic-lease-01.pdf");

        var response = await client.PostAsync("/documents", form);

        // Fails here today (got 501) — the intended RED until ingestion is implemented.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.Equal("synthetic-lease-01.pdf", body.FileName);
        Assert.Equal("ready", body.Status);
        Assert.True(body.ChunkCount > 1, "Expected the document to be split into more than one chunk.");

        Assert.NotEmpty(body.Fields);
        foreach (var expected in new[] { "lessor", "lessee", "legal", "royalty", "date" })
        {
            Assert.Contains(body.Fields, field => field.Name.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }
    }
}
