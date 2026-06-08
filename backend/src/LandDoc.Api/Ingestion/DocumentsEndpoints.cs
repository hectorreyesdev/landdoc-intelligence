using LandDoc.Api.Storage;

namespace LandDoc.Api.Ingestion;

/// <summary>
/// Maps the document ingestion write path (spec 0001, extended by spec 0005):
/// <c>POST /documents</c> accepts one file as multipart/form-data, dispatches on filename extension to
/// either the PDF-parse path (.pdf) or the UTF-8-decode path (.txt / .md / .markdown), runs the ingest
/// pipeline, and returns 201 with the document, its fields, and the chunk count. A missing or empty file,
/// an unsupported extension, or a .pdf whose bytes fail the magic-byte guard returns 400 ProblemDetails
/// and stores nothing.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/documents", async (IFormFile? file, DocumentIngestionService ingestion, CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    title: "A non-empty file is required.",
                    detail: "Send one file as the 'file' part of a multipart/form-data request.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var content = stream.ToArray();

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            DocumentFormat format;
            string contentType;
            if (extension == ".pdf")
            {
                if (!LooksLikePdf(content))
                {
                    return Results.Problem(
                        title: "The uploaded file is not a PDF.",
                        detail: "Only text-based PDF documents are accepted.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
                format = DocumentFormat.Pdf;
                contentType = "application/pdf";
            }
            else if (extension == ".txt")
            {
                format = DocumentFormat.PlainText;
                contentType = "text/plain";
            }
            else if (extension is ".md" or ".markdown")
            {
                format = DocumentFormat.PlainText;
                contentType = "text/markdown";
            }
            else
            {
                return Results.Problem(
                    title: "Unsupported file type.",
                    detail: "Accepted extensions are .pdf, .txt, .md, and .markdown.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var response = await ingestion.IngestAsync(file.FileName, content, format, contentType, cancellationToken);
            return Results.Created($"/documents/{response.Id}", response);
        })
        .DisableAntiforgery();

        // Read-back surface (spec 0006). The original file is served inline so the SPA embeds it in an
        // <iframe>; list returns 200 with [] when nothing is ingested (not 404).
        app.MapGet("/documents", async (IDocumentStore store, CancellationToken cancellationToken) =>
            Results.Ok(await store.ListAsync(cancellationToken)));

        app.MapGet("/documents/{id:guid}", async (Guid id, IDocumentStore store, CancellationToken cancellationToken) =>
        {
            var document = await store.GetAsync(id, cancellationToken);
            return document is null
                ? Results.Problem(title: "Document not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(document);
        });

        app.MapGet("/documents/{id:guid}/file", async (Guid id, IDocumentStore store, CancellationToken cancellationToken) =>
        {
            var file = await store.GetFileAsync(id, cancellationToken);
            return file is null
                ? Results.Problem(title: "Document not found.", statusCode: StatusCodes.Status404NotFound)
                : Results.File(file.Content, file.ContentType);
        });

        return app;
    }

    // PDF files begin with the "%PDF-" header (ISO 32000 §7.5.2) — a cheap guard before handing bytes to the parser.
    private static bool LooksLikePdf(ReadOnlySpan<byte> content) =>
        content.Length >= 5 &&
        content[0] == 0x25 && // %
        content[1] == 0x50 && // P
        content[2] == 0x44 && // D
        content[3] == 0x46 && // F
        content[4] == 0x2D;   // -
}
