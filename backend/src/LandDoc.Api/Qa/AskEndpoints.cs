using LandDoc.Api.Model;
using LandDoc.Api.Retrieval;

namespace LandDoc.Api.Qa;

/// <summary>
/// Maps the RAG Q&amp;A read path (spec 0002): POST /ask retrieves top-k chunks via
/// <see cref="ChunkRetriever"/>, calls the chat adapter for a grounded answer, and returns the answer
/// with citations. Read-only — never mutates the store.
/// </summary>
public static class AskEndpoints
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ask", async (
            AskRequest? request,
            ChunkRetriever retriever,
            IChatClient chat,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The 'question' field is required and must not be empty or whitespace.");
            }

            var topK = await retriever.RetrieveAsync(request.Question, ct);

            if (topK.Count == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: "No documents have been ingested. Ingest at least one document before asking questions.");
            }

            // Map ScoredChunk → QaPassage (chat call) and → Citation (response).
            var passages = topK
                .Select(sc => new QaPassage(sc.Chunk.Id, sc.Chunk.DocumentId, sc.Chunk.Text, sc.Chunk.Source))
                .ToList();

            var answer = await chat.AnswerAsync(request.Question, passages, ct);

            var citations = topK
                .Select(sc => new Citation(sc.Chunk.Id, sc.Chunk.DocumentId, sc.Score, sc.Chunk.Text))
                .ToList();

            return Results.Ok(new AskResponse(answer, citations));
        });

        return app;
    }
}
