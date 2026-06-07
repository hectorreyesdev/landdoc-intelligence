using System.Net;
using System.Net.Http.Json;
using LandDoc.Api.Model;
using LandDoc.Api.Qa;
using LandDoc.Api.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0002 — POST /ask read path: happy path, retrieval correctness, determinism,
/// empty store (409), bad input (400), out-of-corpus anti-hallucination, and read-only invariants.
/// Each test owns its own factory so stores are isolated.
/// </summary>
public sealed class AskEndpointTests
{
    [Fact]
    public async Task Ask_HappyPath_Returns200_WithAnswerAndCitations()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var ingestResponse = await IngestionTestHelpers.PostFixtureAsync(client);
        Assert.Equal(HttpStatusCode.Created, ingestResponse.StatusCode);

        var response = await client.PostAsJsonAsync("/ask", new { question = "Who is the lessee?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AskResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.Answer));
        Assert.NotEmpty(body.Citations);

        // Every citation.chunkId resolves to a stored chunk; each carries documentId, score, and text.
        var stored = await IngestionTestHelpers.StoredChunksAsync(factory);
        var storedIds = stored.Select(c => c.Id).ToHashSet();

        Assert.All(body.Citations, citation =>
        {
            Assert.Contains(citation.ChunkId, storedIds);
            Assert.NotEqual(Guid.Empty, citation.DocumentId);
            Assert.True(double.IsFinite(citation.Score), "citation.Score must be a finite number");
            Assert.False(string.IsNullOrWhiteSpace(citation.Text));
        });
    }

    [Fact]
    public async Task Ask_RetrievalCorrectness_LesseeChunkIsInTopK()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        await IngestionTestHelpers.PostFixtureAsync(client);

        var response = await client.PostAsJsonAsync("/ask", new { question = "Who is the lessee?" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AskResponse>();
        Assert.NotNull(body);

        // At least one cited chunk must contain lessee-related terms from the fixture.
        Assert.Contains(body!.Citations, c =>
            c.Text.Contains("lessee", StringComparison.OrdinalIgnoreCase) ||
            c.Text.Contains("Acme", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ask_DeterministicRetrieval_SameQuestionYieldsSameOrderedChunkIds()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        await IngestionTestHelpers.PostFixtureAsync(client);

        const string question = "Who is the lessee?";

        var r1 = await client.PostAsJsonAsync("/ask", new { question });
        var r2 = await client.PostAsJsonAsync("/ask", new { question });

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var body1 = await r1.Content.ReadFromJsonAsync<AskResponse>();
        var body2 = await r2.Content.ReadFromJsonAsync<AskResponse>();

        Assert.NotNull(body1);
        Assert.NotNull(body2);

        // Same store + same question → same ordered citation chunkIds every time.
        Assert.Equal(
            body1!.Citations.Select(c => c.ChunkId),
            body2!.Citations.Select(c => c.ChunkId));
    }

    [Fact]
    public async Task Ask_EmptyStore_Returns409()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        // No documents ingested — store is empty.
        var response = await client.PostAsJsonAsync("/ask", new { question = "Who is the lessee?" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Ask_MissingQuestion_Returns400()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        // JSON body with no "question" field.
        var response = await client.PostAsJsonAsync("/ask", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_EmptyStringQuestion_Returns400()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ask", new { question = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_WhitespaceQuestion_Returns400()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ask", new { question = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_OutOfCorpusQuestion_AnswerSignalsNotFound_AndStillCites()
    {
        using var factory = new NotFoundChatFactory();
        var client = factory.CreateClient();

        // Ingest via the write path — NotFoundChatFactory also provides canned fields for extraction.
        var ingestResponse = await IngestionTestHelpers.PostFixtureAsync(client);
        Assert.Equal(HttpStatusCode.Created, ingestResponse.StatusCode);

        var response = await client.PostAsJsonAsync("/ask",
            new { question = "What is the offshore platform's water depth?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AskResponse>();
        Assert.NotNull(body);
        // Fake always returns the not-found canned answer.
        Assert.Equal("The answer is not found in the document(s).", body!.Answer);
        // Citations are still ≥1 — the chunks that were searched — each resolving to a stored chunk.
        Assert.NotEmpty(body.Citations);

        // Inline StoredChunksAsync logic — NotFoundChatFactory can't extend the sealed LandDocApiFactory.
        var store = factory.Services.GetRequiredService<IVectorStore>();
        var embedder = factory.Services.GetRequiredService<IEmbeddingClient>();
        var probe = await embedder.EmbedAsync("probe");
        var storedIds = store.TopK(probe, int.MaxValue).Select(sc => sc.Chunk.Id).ToHashSet();

        Assert.All(body.Citations, c => Assert.Contains(c.ChunkId, storedIds));
    }

    [Fact]
    public async Task Ask_ReadOnly_StoreUnchangedAfterMultipleCalls()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        await IngestionTestHelpers.PostFixtureAsync(client);

        var before = await IngestionTestHelpers.StoredChunksAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            var r = await client.PostAsJsonAsync("/ask", new { question = "Who is the lessee?" });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        var after = await IngestionTestHelpers.StoredChunksAsync(factory);

        // Chunk count and IDs must be identical — /ask is read-only, never mutates the store.
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(
            before.Select(c => c.Id),
            after.Select(c => c.Id));

        // Vectors must be identical (same references since the store is unchanged).
        for (var i = 0; i < before.Count; i++)
        {
            Assert.True(before[i].Vector.SequenceEqual(after[i].Vector),
                $"Vector at chunk index {i} changed after /ask calls — store is not read-only.");
        }
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{Program}"/> wired with a chat client whose
    /// <see cref="IChatClient.AnswerAsync"/> always returns a "not found" canned answer, while
    /// <see cref="IChatClient.ExtractFieldsAsync"/> returns the same canned fields as
    /// <see cref="FakeChatClient"/> so ingestion succeeds. Cannot extend the sealed
    /// <see cref="LandDocApiFactory"/>, so storage lookups in this test are inlined.
    /// </summary>
    private sealed class NotFoundChatFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new NotFoundFakeChatClient());
            });
        }

        private sealed class NotFoundFakeChatClient : IChatClient
        {
            public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(
                string documentText,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<ExtractedField> fields =
                [
                    new ExtractedField("Lessor", "John Q. Landowner", null),
                    new ExtractedField("Lessee", "Acme Minerals LLC", null),
                    new ExtractedField("LegalDescription", "Section 14, Block 2, T-1-N, Permian County", null),
                    new ExtractedField("Royalty", "3/16", null),
                    new ExtractedField("EffectiveDate", "2026-01-15", null),
                ];
                return Task.FromResult(fields);
            }

            public Task<string> AnswerAsync(
                string question,
                IReadOnlyList<QaPassage> context,
                CancellationToken cancellationToken = default)
                => Task.FromResult("The answer is not found in the document(s).");
        }
    }
}
