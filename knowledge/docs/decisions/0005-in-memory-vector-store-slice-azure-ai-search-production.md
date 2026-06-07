# 0005. In-memory vector store for the slice; Azure AI Search as production

- Status: Accepted
- Date: 2026-06-06

## Context
Retrieval needs somewhere to hold chunk embeddings and rank them against a query vector. The RAG
pipeline is fixed: ingest → chunk → embed (`IEmbeddingClient`) → store → retrieve top-k → answer
with citations. The forces on *where* those vectors live:

- **This is a vertical slice, not production** — the job is to prove retrieval works end-to-end with
  the simplest thing that does. The demo corpus is tiny *(assumption: a handful of documents, not
  thousands)*, so a linear scan with cosine similarity over `float[]` is fast enough; an
  approximate-nearest-neighbour index or a managed search service would be premature.
- **Self-contained and free is preferred** — consistent with the slice's local-embedding default
  ([[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]), the slice should
  carry no cloud dependency, credentials, or cost just to retrieve.
- **Single process** — the modular monolith ([[knowledge/docs/decisions/0004-modular-monolith-over-microservices]])
  already keeps everything in one process, so an in-process store fits without new infrastructure.
- **Azure AI Search is the named production path but out of scope** — CLAUDE.md lists it as the
  production vector store and explicitly excludes building it now. The data model already records that
  in production chunks + vectors move to Azure AI Search, with "no relational schema or migration
  story here" (DATA-MODEL.md).

Builds on [[knowledge/docs/decisions/0001-record-architecture-decisions]].

## Decision
We will store chunk embeddings in an **in-memory vector store** for the slice: chunks and their
`float[]` vectors held in an in-process collection, with retrieval implemented as **top-k by cosine
similarity via a linear scan**. The store is **not persisted** — it lives for the process lifetime
and is rebuilt by re-ingesting. **Azure AI Search is the designated production path but is out of
scope and not built** in the slice. Retrieval will depend on a narrow store seam *(assumption: a
small `IVectorStore`-style abstraction — not yet defined in code)* mirroring the
`IChatClient` / `IEmbeddingClient` port pattern, so swapping the in-memory store for an Azure AI
Search adapter later is an adapter change rather than a rewrite. The shared-dimension invariant (all
embeddings in a store share one vector length, required for cosine) holds in both worlds.

## Consequences
- **Zero external infrastructure for retrieval.** No service to provision, no credentials, no cost;
  `dotnet run` / `dotnet test` exercise the full retrieval path locally and deterministically.
- **No index build/refresh machinery.** Vectors are usable the moment they're added; nothing to
  reindex.
- **Tradeoff — no persistence.** The store is lost on restart and must be repopulated by
  re-ingesting; there is no durability, concurrency control, or security beyond the process itself.
- **Tradeoff — linear scan won't scale.** Cost is O(n·d) per query; fine for a small demo corpus,
  but it degrades as the corpus grows — which is exactly the boundary where the production path
  (Azure AI Search, with a real vector index) takes over.
- **Production work is deferred, not designed away.** Moving to Azure AI Search — index creation,
  build/refresh, query integration — is out of scope here and will be its own ADR; the store seam is
  the hedge that keeps that swap cheap.
- **Risk:** if the seam is *not* actually introduced in code, the prod swap stops being a clean
  adapter change — worth confirming when Retrieval is built.
