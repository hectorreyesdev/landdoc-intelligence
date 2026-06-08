using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LandDoc.Api.Model;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Storage;

/// <summary>
/// Azure Blob Storage adapter for <see cref="IDocumentStore"/> (ADR-0018). Each document is two blobs in
/// the container: <c>"{id}"</c> holds the original uploaded bytes (with the recorded content type) and
/// <c>"{id}.json"</c> holds the <see cref="DocumentMetadata"/>. Listing enumerates the <c>*.json</c> blobs.
/// Auth is managed-identity-preferred (ADR-0016): a configured <see cref="BlobOptions.ServiceUri"/> uses
/// <see cref="DefaultAzureCredential"/>; otherwise it falls back to <see cref="BlobOptions.ConnectionString"/>
/// (Azurite/local dev, or the provisioned Key Vault secret). Ensures the container exists on construction
/// (idempotent — safe across Container Apps cold starts), mirroring the index-ensure in
/// <see cref="AzureAiSearchVectorStore"/>. Config-selected via <c>DocumentStore:Provider=azureblob</c>; the
/// in-memory store remains the offline/test default.
/// </summary>
public sealed class AzureBlobDocumentStore : IDocumentStore
{
    private readonly BlobContainerClient _container;

    public AzureBlobDocumentStore(IOptions<BlobOptions> options)
    {
        var opts = options.Value;

        BlobServiceClient service;
        if (!string.IsNullOrWhiteSpace(opts.ServiceUri))
        {
            // Passwordless: managed identity in hosting, `az login` locally (DefaultAzureCredential).
            service = new BlobServiceClient(new Uri(opts.ServiceUri), new DefaultAzureCredential());
        }
        else if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            service = new BlobServiceClient(opts.ConnectionString);
        }
        else
        {
            throw new InvalidOperationException(
                "Blob:ServiceUri or Blob:ConnectionString must be set when DocumentStore:Provider is 'azureblob'.");
        }

        _container = service.GetBlobContainerClient(opts.ContainerName);
        _container.CreateIfNotExists();
    }

    public async Task SaveAsync(DocumentMetadata metadata, byte[] originalBytes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(originalBytes);

        // The content type is the source of truth in the metadata JSON (read back in GetFileAsync); we also
        // stamp it on the bytes blob via SetHttpHeaders for correct rendering on direct/portal access.
        var fileBlob = _container.GetBlobClient(metadata.Id.ToString());
        await fileBlob.UploadAsync(BinaryData.FromBytes(originalBytes), overwrite: true, ct);
        await fileBlob.SetHttpHeadersAsync(
            new BlobHttpHeaders { ContentType = metadata.ContentType }, cancellationToken: ct);

        var metaBlob = _container.GetBlobClient(MetadataBlobName(metadata.Id));
        await metaBlob.UploadAsync(BinaryData.FromObjectAsJson(metadata), overwrite: true, ct);
    }

    public async Task<IReadOnlyList<DocumentMetadata>> ListAsync(CancellationToken ct = default)
    {
        var documents = new List<DocumentMetadata>();
        await foreach (var item in _container.GetBlobsAsync(cancellationToken: ct))
        {
            if (!item.Name.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            var blob = _container.GetBlobClient(item.Name);
            var content = await blob.DownloadContentAsync(ct);
            var meta = content.Value.Content.ToObjectFromJson<DocumentMetadata>();
            if (meta is not null)
            {
                documents.Add(meta);
            }
        }

        return documents
            .OrderBy(meta => meta.IngestedAt)
            .ThenBy(meta => meta.Id)
            .ToList();
    }

    public async Task<DocumentMetadata?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(MetadataBlobName(id));
        try
        {
            var content = await blob.DownloadContentAsync(ct);
            return content.Value.Content.ToObjectFromJson<DocumentMetadata>();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<DocumentFile?> GetFileAsync(Guid id, CancellationToken ct = default)
    {
        // Metadata carries the file name + content type; the bytes blob holds the payload.
        var meta = await GetAsync(id, ct);
        if (meta is null)
        {
            return null;
        }

        var blob = _container.GetBlobClient(id.ToString());
        try
        {
            var content = await blob.DownloadContentAsync(ct);
            return new DocumentFile(content.Value.Content.ToArray(), meta.ContentType, meta.FileName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string MetadataBlobName(Guid id) => $"{id}.json";
}
