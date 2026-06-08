using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using LandDoc.Api.Model;
using Microsoft.Extensions.Options;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace LandDoc.Api.Storage;

/// <summary>
/// Azure AI Search adapter for <see cref="IVectorStore"/> (ADR-0017). Uses the Free-tier index
/// <c>landdoc-chunks</c> with HNSW + cosine. Ensures the index exists on construction (idempotent —
/// safe across Container Apps cold starts). Uses the asynchronous Azure SDK overloads end-to-end so
/// network I/O never blocks a thread, satisfying the async <see cref="IVectorStore"/> contract.
/// Config-selected via <c>VectorStore:Provider=azuresearch</c>; the in-memory store remains the
/// offline/test default.
/// </summary>
public sealed class AzureAiSearchVectorStore : IVectorStore
{
    private readonly SearchClient _searchClient;

    public AzureAiSearchVectorStore(
        IOptions<Model.SearchOptions> searchOptions,
        IOptions<EmbeddingOptions> embeddingOptions)
    {
        var opts = searchOptions.Value;
        var dimension = embeddingOptions.Value.Dimension;

        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            throw new InvalidOperationException(
                "Search:Endpoint must be set when VectorStore:Provider is 'azuresearch'.");
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "Search:ApiKey must be set when VectorStore:Provider is 'azuresearch'.");

        var endpoint = new Uri(opts.Endpoint);
        var credential = new AzureKeyCredential(opts.ApiKey);

        _searchClient = new SearchClient(endpoint, opts.IndexName, credential);

        EnsureIndex(endpoint, credential, opts.IndexName, dimension);
    }

    public async Task AddAsync(Chunk chunk, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var doc = new SearchDocument
        {
            ["id"] = chunk.Id.ToString(),
            ["documentId"] = chunk.DocumentId.ToString(),
            ["text"] = chunk.Text,
            ["source"] = chunk.Source,
            ["contentVector"] = chunk.Vector,
        };

        await _searchClient.IndexDocumentsAsync(
            IndexDocumentsBatch.MergeOrUpload(new[] { doc }), cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ScoredChunk>> TopKAsync(float[] query, int k, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0) return [];

        var vectorQuery = new VectorizedQuery(new ReadOnlyMemory<float>(query))
        {
            KNearestNeighborsCount = k,
            Exhaustive = false,
        };
        vectorQuery.Fields.Add("contentVector");

        var azureOptions = new AzureSearchOptions { Size = k };
        azureOptions.VectorSearch = new VectorSearchOptions();
        azureOptions.VectorSearch.Queries.Add(vectorQuery);
        azureOptions.Select.Add("id");
        azureOptions.Select.Add("documentId");
        azureOptions.Select.Add("text");
        azureOptions.Select.Add("source");

        var response = await _searchClient.SearchAsync<SearchDocument>(
            searchText: null, options: azureOptions, cancellationToken: ct);

        return response.Value.GetResults()
            .Select(r =>
            {
                var doc = r.Document;
                var chunk = new Chunk(
                    Id: Guid.Parse((string)doc["id"]),
                    DocumentId: Guid.Parse((string)doc["documentId"]),
                    Text: (string)doc["text"] ?? string.Empty,
                    Vector: [],
                    Source: (string)doc["source"] ?? string.Empty);
                return new ScoredChunk(chunk, r.Score ?? 0);
            })
            .ToList();
    }

    private static void EnsureIndex(Uri endpoint, AzureKeyCredential credential, string indexName, int dimension)
    {
        var indexClient = new SearchIndexClient(endpoint, credential);

        var index = new SearchIndex(indexName)
        {
            Fields =
            {
                new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
                new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
                new SearchableField("text"),
                new SimpleField("source", SearchFieldDataType.String) { IsFilterable = true },
                new VectorSearchField("contentVector", vectorSearchDimensions: dimension, vectorSearchProfileName: "default-hnsw-profile"),
            },
            VectorSearch = new VectorSearch
            {
                Algorithms = { new HnswAlgorithmConfiguration("default-hnsw") },
                Profiles = { new VectorSearchProfile("default-hnsw-profile", "default-hnsw") },
            },
        };

        indexClient.CreateOrUpdateIndex(index);
    }
}
