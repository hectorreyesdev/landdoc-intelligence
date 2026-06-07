using LandDoc.Api.Extraction;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Orchestrates the ingest write path (spec 0001): parse PDF text → extract fields (via the Extraction
/// module) → chunk with overlap → embed each chunk (<see cref="IEmbeddingClient"/>) → store the chunks in
/// the shared <see cref="IVectorStore"/>. Returns the new document id, its extracted fields, and the
/// number of chunks stored. Every value is produced by the pipeline — nothing is keyed to a document.
/// </summary>
public sealed class DocumentIngestionService(
    PdfTextExtractor pdfTextExtractor,
    FieldExtractor fieldExtractor,
    TextChunker textChunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore)
{
    public async Task<IngestDocumentResponse> IngestAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var documentId = Guid.NewGuid();

        var text = pdfTextExtractor.Extract(content);
        var fields = await fieldExtractor.ExtractAsync(text, cancellationToken);

        var chunkTexts = textChunker.Chunk(text);
        foreach (var chunkText in chunkTexts)
        {
            var vector = await embeddingClient.EmbedAsync(chunkText, cancellationToken);
            vectorStore.Add(new Chunk(Guid.NewGuid(), documentId, chunkText, vector));
        }

        return new IngestDocumentResponse(documentId, fileName, "ready", fields, chunkTexts.Count);
    }
}
