# 0004 — Extract a Retrieval service (read-path retrieval seam)

**Status:** Accepted

## What to build
A **pure refactor** of the spec-0002 read path: move the inline embed→top-k retrieval out of
`Qa/AskEndpoints` into a dedicated **`Retrieval`** module service, so the code matches the boundary
ARCHITECTURE.md already documents (*"`Retrieval` — question → embed → top-k chunks from the store"*).

Today the `/ask` handler does the retrieval mechanics inline — embed the query via `IEmbeddingClient`,
then `IVectorStore.TopK(queryVector, k)` with `k` from `RetrievalOptions`. This spec introduces a
`ChunkRetriever` (namespace `LandDoc.Api.Retrieval`) that owns exactly that — *question → embed →
`TopK` → `IReadOnlyList<ScoredChunk>`* — and has the `/ask` handler depend on it, then map the results
→ `QaPassage` (chat) / `Citation` (response) and call `IChatClient` as before. It makes the read path
symmetric with the write path (which already has `DocumentIngestionService`) and makes retrieval
independently unit-testable.

**No behavior or contract change:** same `POST /ask` request/response, same `200` / `400` (blank
question) / `409` (empty store), same citations and ordering.

## Constraints
- **Backend:** .NET 10 modular monolith (ADR-0004); C# conventions per `CLAUDE.md` (nullable, async
  end-to-end, constructor DI, file-scoped namespaces, `sealed`/`record` types).
- **No public-port change.** `IChatClient`, `IEmbeddingClient`, `IVectorStore` stay exactly as-is —
  this is an internal seam, not a port (no `knowledge/docs/specs/` port-change gate is triggered).
- **The new type:** `ChunkRetriever` in `Retrieval/`, constructor-injected with `IEmbeddingClient` +
  `IVectorStore` + `IOptions<RetrievalOptions>`, exposing one method:
  `Task<IReadOnlyList<ScoredChunk>> RetrieveAsync(string question, CancellationToken ct = default)`
  that embeds the question and returns `store.TopK(queryVector, options.TopK)`. Registered in DI.
- **`/ask` handler** injects `ChunkRetriever` in place of `IEmbeddingClient` + `IVectorStore`; the
  empty-store `409` becomes an inspection of the retriever result (`Count == 0`). The
  `ScoredChunk → QaPassage`/`Citation` mapping and the `IChatClient.AnswerAsync` call **stay in the
  handler** (Qa orchestration).
- **Do not modify any existing passing test.** The 18 existing tests (incl. the `/ask` integration
  suite) stay green and keep exercising the same behavior through the endpoint.
- **Out of scope:** reranking / ANN indexing / Azure AI Search · any change to `k`'s default or the
  cosine / deterministic-tie-break semantics · multi-turn · the Foundry→Anthropic failover (ADR-0007).

## How to verify
- **Suite green (tdd):** `dotnet build` + `dotnet test` pass, **including the existing 18** — the `/ask`
  integration tests prove the endpoint behavior is unchanged by the refactor.
- **New unit test(s) for `ChunkRetriever`** (new file; inject the real `LocalEmbeddingClient` +
  `InMemoryVectorStore`, or a small fake): with a populated store, `RetrieveAsync` returns the top-k
  `ScoredChunk`s in deterministic order and **respects `Retrieval:TopK`** (e.g. k=2 → 2 results); an
  **empty store → empty list** (the handler turns that into `409`).
- **Structural check:** the `EmbedAsync` + `TopK` calls no longer appear in `Qa/AskEndpoints`; they
  live in `Retrieval/ChunkRetriever.cs`. ARCHITECTURE.md's "`Retrieval` module" description now matches
  the code (closes drift item #4 from the 2026-06-07 reconcile).
- **CI green** on the PR (the locked-mode build/test gate).

## Links
- **Refactors:** [[knowledge/docs/specs/0002-rag-qa-with-citations]] — the read path whose inline
  retrieval this extracts.
- **ADRs:** [[knowledge/docs/decisions/0004-modular-monolith-over-microservices]] (the module boundary
  this aligns the code to) · [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]
  (the `IVectorStore.TopK` seam) · [[knowledge/docs/decisions/0009-corpus-wide-ask-retrieval-scope]].
- **Origin:** drift item #4, 2026-06-07 `/reconcile` of the spec-0002 read path (doc was canonical →
  conform the code).
- **Implementing PR:** _TBD — link once opened._
