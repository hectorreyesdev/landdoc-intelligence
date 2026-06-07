namespace LandDoc.Api.Qa;

/// <summary>
/// Maps the RAG Q&amp;A read path (spec 0002). Unimplemented in the skeleton — the endpoint exists and
/// returns <c>501 Not Implemented</c> as RFC 7807 ProblemDetails. The real handler (embed query →
/// top-k → grounded answer with citations) lands with the retrieval + Qa modules.
/// </summary>
public static class AskEndpoints
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ask", () => Results.Problem(statusCode: StatusCodes.Status501NotImplemented));
        return app;
    }
}
