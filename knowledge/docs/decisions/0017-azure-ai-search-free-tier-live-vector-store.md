# 0017. Azure AI Search (Free tier) as the live vector store

- Status: Accepted
- Date: 2026-06-08

## Context
[[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]] chose an
in-memory vector store for the slice and named Azure AI Search as the production target, explicitly
deferring it to "its own ADR." This is that ADR.

In-memory dies on restart and is single-node — fine for the slice, but the app now needs
**persistence** (ingested docs + their chunks/answers surviving restart and redeploy) and a
foundation for the document list (Step 2) and original files (Step 3). We realize ADR-0005's named
target now at the smallest cost: **Azure AI Search Free tier** — $0, one per subscription,
50 MB / 3 indexes, far beyond a 36-doc / few-hundred-chunk corpus. At this scale approximate-nearest-
neighbour isn't needed for speed; the value is **persistence + the Azure-native path**
(Antero / MS-shop narrative).

Constraints that shape the choice, verified against the current code and the Free-tier limits:
- The store seam exists as designed — `IVectorStore` with `Add(Chunk)` / `TopK(float[], int)`
  (`backend/src/LandDoc.Api/Storage/`), consumed by `DocumentIngestionService` (write) and
  `ChunkRetriever` (read). **But the port is synchronous today**, and `Azure.Search.Documents` is
  async-first; the repo mandates `async`/`await` end-to-end (never `.Result` / `.Wait()`). So the
  swap is *not* a pure adapter change — it requires async-ifying the seam first (owned here; see
  Decision).
- The vector length is pinned at `Embedding:Dimension` = 256 (`appsettings.json`), and the live
  embedder is Azure OpenAI `text-embedding-3-small`
  ([[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]).
- The store is currently registered directly (`AddSingleton<IVectorStore, InMemoryVectorStore>()`),
  **not** behind a provider switch — unlike `IEmbeddingClient` / `IChatClient`, which select an
  adapter from a `ModelClient:*Provider` config switch
  ([[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]). This ADR
  introduces the matching `VectorStore:Provider` switch.
- Free tier is **API-key auth only (no managed identity)** and has **no semantic ranker** — neither
  matters here (vector search itself is unaffected; the slice doesn't use semantic ranking). It is
  provisioned in **eastus** (Free capacity was out in eastus2; cross-region latency from the eastus2
  app stack is negligible at this corpus size).

Builds on [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]
(realizes/refines it — the principle stands; this picks the concrete service and tier) and
[[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]] (where the
admin key is sourced).

## Decision
We will make **Azure AI Search (Free tier) the config-selected live vector store**, in two parts.

**1. Async-ify the `IVectorStore` seam (a deliberate port change, recorded here).** The current
synchronous port cannot host an async Azure SDK adapter without blocking, so we change it to
`Task AddAsync(Chunk, CancellationToken)` / `Task<IReadOnlyList<ScoredChunk>> TopKAsync(float[], int,
CancellationToken)`, and update the in-memory implementation plus both callers
(`DocumentIngestionService`, `ChunkRetriever`) to await it. This is a public-port change — the
guardrails require it be written down before code; **this ADR is that record**, in lieu of a
separate spec.

**2. Add an `AzureAiSearchVectorStore : IVectorStore` adapter** (`Azure.Search.Documents`) over a
**`landdoc-chunks`** index: key = chunk id; fields `documentId`, `text`, `source`; a vector field
sized to `Embedding:Dimension` (256) with HNSW + cosine. The adapter ensures the index exists on
startup (idempotent). Introduce a `VectorStore:Provider` config switch (mirroring
`ModelClient:*Provider`): `azuresearch` becomes the **live default**, `inmemory` stays the
**offline/test** provider. Auth via an **admin key** from config / Key Vault (Free tier has no
managed identity). Tests pin `VectorStore:Provider=inmemory` assembly-wide via the existing
`TestModuleInitializer` (which already pins `ModelClient__EmbeddingProvider=local`) so CI — which has
no Search credentials — stays green. The `/ask` contract and the Q&A / extraction code are unchanged.

## Consequences
- Ingested chunks (and the answers grounded on them) **persist across restarts and redeploys**; one
  Azure service, **$0**.
- **The port becomes async** — a real, if small, breaking change to `IVectorStore` and its two
  callers. Owned here; the in-memory adapter wraps its synchronous work in completed tasks.
- Embedding model/dimension changes invalidate the index → **re-ingest** (the 256-d vector field is
  fixed at index-create). Keep the **same embedder for index and query** (both live Azure) — the
  cosine invariant from ADR-0005 still holds.
- Free tier limits accepted: admin **key lives in Key Vault / secrets, not managed identity**; **no
  semantic ranker**; **50 MB** cap (ample for this corpus). Provisioned in **eastus**, cross-region
  from the eastus2 stack (negligible at this scale).
- The provider is **config-selected with a live default of `azuresearch`**, so the production default
  is now part of the test surface — tests **must** pin `inmemory`, or CI (no Search creds) breaks.
  This is the green-locally / red-in-CI trap; the `TestModuleInitializer` pin is the guard.
- **Production-at-scale is a tier/config change, not code:** the same index on Basic+ adds managed
  identity, semantic ranker, and scale-out — selected via config, no adapter rewrite.

## Notes for implementation (non-binding)
- New NuGet dependency: `Azure.Search.Documents`.
- New config keys: `VectorStore:Provider`, plus the Search endpoint and `kv-landdoc-hr01` admin-key
  secret reference (per ADR-0016). A spec/issue can carry the implementation detail.
