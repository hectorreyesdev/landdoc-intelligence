using System.Net;
using System.Text;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 bad-input behavior: a missing, empty, or non-PDF upload returns 400 as RFC 7807
/// ProblemDetails and stores nothing. Each test uses its own factory so the store starts empty.
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
    public async Task NonPdfFile_Returns400ProblemDetails_NothingStored()
    {
        using var factory = new LandDocApiFactory();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("This is plainly not a PDF.")), "file", "notes.txt");
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
