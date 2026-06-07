using LandDoc.Api.Model;
using LandDoc.Api.Retrieval;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0004 — ChunkRetriever unit tests: empty store returns empty list, populated store returns
/// top-k in deterministic order, TopK config is respected.
/// </summary>
public sealed class ChunkRetrieverTests
{
    private static LocalEmbeddingClient CreateEmbedder() =>
        new(Options.Create(new EmbeddingOptions { Dimension = 256 }));

    private static ChunkRetriever CreateRetriever(IEmbeddingClient embedder, IVectorStore store, int topK) =>
        new(embedder, store, Options.Create(new RetrievalOptions { TopK = topK }));

    [Fact]
    public async Task RetrieveAsync_EmptyStore_ReturnsEmptyList()
    {
        var retriever = CreateRetriever(CreateEmbedder(), new InMemoryVectorStore(), topK: 5);

        var result = await retriever.RetrieveAsync("Who is the lessee?");

        Assert.Empty(result);
    }

    [Fact]
    public async Task RetrieveAsync_PopulatedStore_RespectsTopK()
    {
        var embedder = CreateEmbedder();
        var store = new InMemoryVectorStore();

        for (var i = 0; i < 3; i++)
        {
            var text = $"Lease clause {i}: the lessee shall pay royalties of one-eighth.";
            var vector = await embedder.EmbedAsync(text);
            store.Add(new Chunk(Guid.NewGuid(), Guid.NewGuid(), text, vector));
        }

        var retriever = CreateRetriever(embedder, store, topK: 2);

        var result = await retriever.RetrieveAsync("Who is the lessee?");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RetrieveAsync_PopulatedStore_ReturnsDeterministicOrder()
    {
        var embedder = CreateEmbedder();
        var store = new InMemoryVectorStore();

        for (var i = 0; i < 4; i++)
        {
            var text = $"Section {i} grants rights to the lessee for mineral extraction.";
            var vector = await embedder.EmbedAsync(text);
            store.Add(new Chunk(Guid.NewGuid(), Guid.NewGuid(), text, vector));
        }

        var retriever = CreateRetriever(embedder, store, topK: 3);
        const string question = "Who is the lessee?";

        var result1 = await retriever.RetrieveAsync(question);
        var result2 = await retriever.RetrieveAsync(question);

        Assert.Equal(result1.Select(s => s.Chunk.Id), result2.Select(s => s.Chunk.Id));
    }

    [Fact]
    public async Task RetrieveAsync_PopulatedStore_ScoresDescending()
    {
        var embedder = CreateEmbedder();
        var store = new InMemoryVectorStore();

        for (var i = 0; i < 3; i++)
        {
            var text = $"Document {i}: the royalty rate is three-sixteenths.";
            var vector = await embedder.EmbedAsync(text);
            store.Add(new Chunk(Guid.NewGuid(), Guid.NewGuid(), text, vector));
        }

        var retriever = CreateRetriever(embedder, store, topK: 5);

        var result = await retriever.RetrieveAsync("What is the royalty rate?");

        Assert.NotEmpty(result);
        for (var i = 0; i < result.Count - 1; i++)
        {
            Assert.True(result[i].Score >= result[i + 1].Score,
                $"Score at index {i} ({result[i].Score}) must be >= index {i + 1} ({result[i + 1].Score})");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RetrieveAsync_BlankQuestion_Throws(string? question)
    {
        var retriever = CreateRetriever(CreateEmbedder(), new InMemoryVectorStore(), topK: 5);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => retriever.RetrieveAsync(question!));
    }
}
