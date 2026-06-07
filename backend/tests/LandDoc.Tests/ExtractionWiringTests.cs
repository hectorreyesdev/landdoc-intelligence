using System.Net;
using System.Net.Http.Json;
using LandDoc.Api.Ingestion;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0001 extraction wiring: the fields the (fake) <c>IChatClient</c> returns appear verbatim in the
/// response, proving the Extraction module calls the port and maps its result — rather than parsing the
/// document itself or hardcoding values.
/// </summary>
public sealed class ExtractionWiringTests
{
    [Fact]
    public async Task Ingest_ReturnsExactFieldsProducedByChatClient()
    {
        using var factory = new LandDocApiFactory(); // swaps in FakeChatClient
        var response = await IngestionTestHelpers.PostFixtureAsync(factory.CreateClient());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
        Assert.NotNull(body);

        // These are exactly what FakeChatClient returns; they must flow through unchanged.
        Assert.Equal(5, body!.Fields.Count);
        Assert.Contains(body.Fields, field => field is { Name: "Lessor", Value: "John Q. Landowner" });
        Assert.Contains(body.Fields, field => field is { Name: "Lessee", Value: "Acme Minerals LLC" });
        Assert.Contains(body.Fields, field => field is { Name: "Royalty", Value: "3/16" });
    }
}
