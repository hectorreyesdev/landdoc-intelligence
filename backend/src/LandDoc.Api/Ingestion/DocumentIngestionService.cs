using System.Text;
using LandDoc.Api.Extraction;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Logging;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Orchestrates the ingest write path (spec 0001, 0006): parse PDF text → extract fields (via the
/// Extraction module) → chunk with overlap → embed each chunk (<see cref="IEmbeddingClient"/>) → store the
/// chunks in the shared <see cref="IVectorStore"/> → persist the document (original bytes + metadata +
/// fields) in <see cref="IDocumentStore"/>. Returns the new document id, its extracted fields, and the
/// number of chunks stored. Field extraction is **best-effort** (spec 0001 amendment): a failing
/// <see cref="IChatClient"/> provider degrades to empty fields, it never fails the write path. Document
/// persistence, by contrast, is **required** (ADR-0018): a store failure fails the write path and the
/// just-written chunks are rolled back (compensated) so no orphan chunks survive a failed ingest.
/// </summary>
public sealed class DocumentIngestionService(
    PdfTextExtractor pdfTextExtractor,
    FieldExtractor fieldExtractor,
    TextChunker textChunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    IDocumentStore documentStore,
    ILogger<DocumentIngestionService> logger)
{
    public async Task<IngestDocumentResponse> IngestAsync(string fileName, byte[] content, DocumentFormat format, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var documentId = Guid.NewGuid();

        var text = format switch
        {
            DocumentFormat.Pdf => pdfTextExtractor.Extract(content),
            DocumentFormat.PlainText => Encoding.UTF8.GetString(content),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognised document format."),
        };

        // Field extraction is best-effort (spec 0001 amendment): the chat provider may be unavailable
        // (missing key, unreachable gateway, parse error) but ingest must still store the chunks. On
        // failure we log and return empty fields rather than 500-ing the write path. Cancellation is
        // a real failure of the request, not a provider hiccup — let it propagate.
        IReadOnlyList<ExtractedField> fields;
        try
        {
            fields = await fieldExtractor.ExtractAsync(text, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Field extraction failed for {FileName}; storing chunks with empty fields.", fileName);
            fields = [];
        }

        var safeSource = SanitizeSource(fileName);

        var chunkTexts = textChunker.Chunk(text);
        foreach (var chunkText in chunkTexts)
        {
            var vector = await embeddingClient.EmbedAsync(chunkText, cancellationToken);
            await vectorStore.AddAsync(new Chunk(Guid.NewGuid(), documentId, chunkText, vector, safeSource), cancellationToken);
        }

        // Persist the document (original bytes + metadata + extracted fields) so it can be listed and the
        // source file viewed (ADR-0018). Unlike best-effort extraction this is *required*: if we can't store
        // the document, the "view source" feature is broken — so a store failure fails the write path. But
        // the chunks are already committed at this point, so on failure we compensate by deleting them
        // before rethrowing — otherwise /ask would retrieve orphan chunks with no viewable source, and a
        // retried upload (new documentId) would silently accumulate duplicates. The compensation is
        // best-effort: a cleanup failure is logged but must not mask the original store error.
        var metadata = new DocumentMetadata(documentId, fileName, "ready", contentType, chunkTexts.Count, fields, DateTimeOffset.UtcNow);
        try
        {
            await documentStore.SaveAsync(metadata, content, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Document store failed for {FileName}; rolling back {ChunkCount} orphaned chunks for {DocumentId}.", fileName, chunkTexts.Count, documentId);
            try
            {
                await vectorStore.DeleteByDocumentAsync(documentId, cancellationToken);
            }
            catch (Exception cleanupEx) when (cleanupEx is not OperationCanceledException)
            {
                logger.LogError(cleanupEx, "Compensating chunk delete failed for {DocumentId}; its chunks may be orphaned.", documentId);
            }
            throw;
        }

        return new IngestDocumentResponse(documentId, fileName, "ready", fields, chunkTexts.Count);
    }

    // Sanitize filenames before they flow into the LLM grounding prompt: newlines break the
    // Content-Disposition header and can inject prompt instructions; brackets break [Source: …] labels.
    internal static string SanitizeSource(string fileName)
    {
        var safe = fileName
            .ReplaceLineEndings(" ")
            .Replace("[", "(")
            .Replace("]", ")")
            .Trim();
        return safe.Length > 200 ? safe[..200] : safe;
    }
}
