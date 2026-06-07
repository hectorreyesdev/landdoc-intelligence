namespace LandDoc.Api.Ingestion;

/// <summary>
/// Maps the document ingestion write path (spec 0001): <c>POST /documents</c> accepts one PDF as
/// multipart/form-data, runs the ingest pipeline, and returns 201 with the document, its fields, and the
/// chunk count. A missing, empty, or non-PDF upload returns 400 as RFC 7807 ProblemDetails and stores
/// nothing.
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
                    title: "A non-empty PDF file is required.",
                    detail: "Send one PDF as the 'file' part of a multipart/form-data request.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var content = stream.ToArray();

            if (!LooksLikePdf(content))
            {
                return Results.Problem(
                    title: "The uploaded file is not a PDF.",
                    detail: "Only text-based PDF documents are accepted.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var response = await ingestion.IngestAsync(file.FileName, content, cancellationToken);
            return Results.Created($"/documents/{response.Id}", response);
        })
        .DisableAntiforgery();

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
