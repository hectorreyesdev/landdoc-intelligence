using System.Net;
using System.Text;

namespace LandDoc.Tests;

/// <summary>
/// Bad-input behavior: a missing, empty, or unsupported-extension upload returns 400 as RFC 7807
/// ProblemDetails and stores nothing. Each test uses its own factory so the store starts empty.
/// Spec 0005 supersedes 0001's "non-PDF → 400" with "unsupported extension → 400"; .txt/.md/.markdown
/// are now accepted and no longer trigger 400.
/// </summary>
public sealed class BadInputTests
{
    [Fact]
    public async Task MissingFile_Returns400ProblemDetails_NothingStored()
    {
        using var factory = new LandDocApiFactory();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("no file here"), "note"); // no "file" part
        var response = await factory.CreateClient().PostAsync("/documents", form);

        await AssertBadRequestAndEmptyStoreAsync(response, factory);
    }

    [Fact]
    public async Task EmptyFile_Returns400ProblemDetails_NothingStored()
    {
        using var factory = new LandDocApiFactory();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([]), "file", "empty.pdf");
        var response = await factory.CreateClient().PostAsync("/documents", form);

        await AssertBadRequestAndEmptyStoreAsync(response, factory);
    }

    [Fact]
    public async Task UnsupportedExtension_Returns400ProblemDetails_NothingStored()
    {
        using var factory = new LandDocApiFactory();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("some content")), "file", "notes.docx");
        var response = await factory.CreateClient().PostAsync("/documents", form);

        await AssertBadRequestAndEmptyStoreAsync(response, factory);
    }

    private static async Task AssertBadRequestAndEmptyStoreAsync(HttpResponseMessage response, LandDocApiFactory factory)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await IngestionTestHelpers.StoredChunksAsync(factory));
    }
}
