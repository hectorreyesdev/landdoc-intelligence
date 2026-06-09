using LandDoc.Api.Model;
using LandDoc.Api.Storage;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0006 / ADR-0018 — the <see cref="IDocumentStore"/> contract, verified against the
/// <see cref="InMemoryDocumentStore"/> (the offline adapter; the Azure Blob adapter shares the same
/// contract). Save → list/get/getfile round-trips, unknown ids return null, empty store lists empty.
/// </summary>
public sealed class DocumentStoreTests
{
    private static DocumentMetadata Meta(Guid id, string fileName, string contentType, int chunkCount) =>
        new(
            id,
            fileName,
            "ready",
            contentType,
            chunkCount,
            [new ExtractedField("Lessee", "Acme Minerals LLC", null)],
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task Save_then_Get_returnsSameMetadata()
    {
        var store = new InMemoryDocumentStore();
        var id = Guid.NewGuid();
        var meta = Meta(id, "lease.pdf", "application/pdf", 4);

        await store.SaveAsync(meta, [1, 2, 3]);

        var got = await store.GetAsync(id);
        Assert.NotNull(got);
        Assert.Equal(meta, got);
    }

    [Fact]
    public async Task Save_then_GetFile_returnsSameBytesAndContentType()
    {
        var store = new InMemoryDocumentStore();
        var id = Guid.NewGuid();
        byte[] bytes = [0x25, 0x50, 0x44, 0x46];
        await store.SaveAsync(Meta(id, "lease.pdf", "application/pdf", 1), bytes);

        var file = await store.GetFileAsync(id);

        Assert.NotNull(file);
        Assert.Equal(bytes, file!.Content);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("lease.pdf", file.FileName);
    }

    [Fact]
    public async Task Get_unknownId_returnsNull()
    {
        var store = new InMemoryDocumentStore();
        Assert.Null(await store.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetFile_unknownId_returnsNull()
    {
        var store = new InMemoryDocumentStore();
        Assert.Null(await store.GetFileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task List_emptyStore_returnsEmpty()
    {
        var store = new InMemoryDocumentStore();
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task List_returnsAllSavedDocuments()
    {
        var store = new InMemoryDocumentStore();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await store.SaveAsync(Meta(id1, "a.pdf", "application/pdf", 2), [1]);
        await store.SaveAsync(Meta(id2, "b.md", "text/markdown", 3), [2]);

        var all = await store.ListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.Id == id1 && m.FileName == "a.pdf");
        Assert.Contains(all, m => m.Id == id2 && m.FileName == "b.md");
    }

    [Fact]
    public async Task Delete_removesMetadataAndFile_andOmitsFromList()
    {
        var store = new InMemoryDocumentStore();
        var id = Guid.NewGuid();
        await store.SaveAsync(Meta(id, "lease.pdf", "application/pdf", 2), [1, 2]);

        await store.DeleteAsync(id);

        Assert.Null(await store.GetAsync(id));
        Assert.Null(await store.GetFileAsync(id));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Delete_unknownId_isNoOp()
    {
        var store = new InMemoryDocumentStore();
        await store.DeleteAsync(Guid.NewGuid()); // must not throw
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Save_sameId_overwrites()
    {
        var store = new InMemoryDocumentStore();
        var id = Guid.NewGuid();
        await store.SaveAsync(Meta(id, "v1.pdf", "application/pdf", 1), [1]);
        await store.SaveAsync(Meta(id, "v2.pdf", "application/pdf", 2), [2]);

        var all = await store.ListAsync();
        Assert.Single(all);
        Assert.Equal("v2.pdf", all[0].FileName);
    }
}
