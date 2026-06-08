using System.Text;
using LandDoc.Api.Extraction;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Logging;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Orchestrates the ingest write path (spec 0001): parse PDF text → extract fields (via the Extraction
/// module) → chunk with overlap → embed each chunk (<see cref="IEmbeddingClient"/>) → store the chunks in
/// the shared <see cref="IVectorStore"/>. Returns the new document id, its extracted fields, and the
/// number of chunks stored. Every value is produced by the pipeline — nothing is keyed to a document.
/// Field extraction is **best-effort** (spec 0001 amendment): a failing <see cref="IChatClient"/> provider
/// degrades to empty fields, it never fails the write path.
/// </summary>
public sealed class DocumentIngestionService(
    PdfTextExtractor pdfTextExtractor,
    FieldExtractor fieldExtractor,
    TextChunker textChunker,
    IEmbeddingClient embeddingClient,
    IVectorStore vectorStore,
    ILogger<DocumentIngestionService> logger)
{
    public async Task<IngestDocumentResponse> IngestAsync(string fileName, byte[] content, DocumentFormat format, CancellationToken cancellationToken = default)
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

        // Sanitize once before the loop: filenames flow into the LLM grounding prompt and a crafted
        // name containing newlines or bracket characters can break the [Source: …] label structure
        // and inject arbitrary instructions. The original fileName is kept for the UI response
        // (React escapes it); only the value that reaches the prompt needs sanitizing.
        var safeSource = fileName
            .ReplaceLineEndings(" ")
            .Replace("[", "(")
            .Replace("]", ")")
            .Trim();
        if (safeSource.Length > 200)
            safeSource = safeSource[..200];

        var chunkTexts = textChunker.Chunk(text);
        foreach (var chunkText in chunkTexts)
        {
            var vector = await embeddingClient.EmbedAsync(chunkText, cancellationToken);
            vectorStore.Add(new Chunk(Guid.NewGuid(), documentId, chunkText, vector, safeSource));
        }

        return new IngestDocumentResponse(documentId, fileName, "ready", fields, chunkTexts.Count);
    }
}
