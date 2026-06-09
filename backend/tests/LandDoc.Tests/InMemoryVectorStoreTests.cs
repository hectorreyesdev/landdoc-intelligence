using LandDoc.Api.Storage;

namespace LandDoc.Tests;

/// <summary>Spec 0008 — chunk deletion by document on the in-memory vector store (the Azure adapter shares the contract).</summary>
public sealed class InMemoryVectorStoreTests
{
    private static Chunk Chunk(Guid documentId) =>
        new(Guid.NewGuid(), documentId, "text", [0.1f, 0.2f, 0.3f], "src.pdf");

    [Fact]
    public async Task DeleteByDocument_removesOnlyThatDocumentsChunks()
    {
        var store = new InMemoryVectorStore();
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        await store.AddAsync(Chunk(docA));
        await store.AddAsync(Chunk(docA));
        await store.AddAsync(Chunk(docB));

        await store.DeleteByDocumentAsync(docA);

        var remaining = await store.TopKAsync([0.1f, 0.2f, 0.3f], int.MaxValue);
        Assert.All(remaining, scored => Assert.Equal(docB, scored.Chunk.DocumentId));
        Assert.Single(remaining);
    }

    [Fact]
    public async Task DeleteByDocument_unknownId_isNoOp()
    {
        var store = new InMemoryVectorStore();
        await store.AddAsync(Chunk(Guid.NewGuid()));

        await store.DeleteByDocumentAsync(Guid.NewGuid()); // must not throw

        var remaining = await store.TopKAsync([0.1f, 0.2f, 0.3f], int.MaxValue);
        Assert.Single(remaining);
    }
}
