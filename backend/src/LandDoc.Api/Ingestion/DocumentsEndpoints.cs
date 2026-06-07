namespace LandDoc.Api.Ingestion;

/// <summary>
/// Maps the document ingestion write path (spec 0001). Unimplemented in the skeleton — the endpoint
/// exists and returns <c>501 Not Implemented</c> as RFC 7807 ProblemDetails. The real handler (multipart
/// <c>[FromForm] IFormFile</c> + <c>.DisableAntiforgery()</c>, parse → extract → chunk → embed → store)
/// lands with the ingest pipeline.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/documents", () => Results.Problem(statusCode: StatusCodes.Status501NotImplemented));
        return app;
    }
}
