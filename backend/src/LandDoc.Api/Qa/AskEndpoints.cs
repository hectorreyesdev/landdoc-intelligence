using LandDoc.Api.Model;
using LandDoc.Api.Retrieval;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Qa;

/// <summary>
/// Maps the RAG Q&amp;A read path (spec 0002): POST /ask embeds the query, retrieves top-k chunks from
/// the shared store, calls the chat adapter for a grounded answer, and returns the answer with
/// citations. Read-only — never mutates the store.
/// </summary>
public static class AskEndpoints
{
    public static IEndpointRouteBuilder MapAskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ask", async (
            AskRequest? request,
            IEmbeddingClient embedder,
            IVectorStore store,
            IChatClient chat,
            IOptions<RetrievalOptions> retrievalOptions,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Question))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The 'question' field is required and must not be empty or whitespace.");
            }

            var queryVector = await embedder.EmbedAsync(request.Question, ct);
            var k = retrievalOptions.Value.TopK;
            var topK = store.TopK(queryVector, k);

            if (topK.Count == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: "No documents have been ingested. Ingest at least one document before asking questions.");
            }

            // Map ScoredChunk → QaPassage (chat call) and → Citation (response).
            var passages = topK
                .Select(sc => new QaPassage(sc.Chunk.Id, sc.Chunk.DocumentId, sc.Chunk.Text))
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
