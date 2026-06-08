# 0001 — Document Ingestion (Write Path)

**Status:** Accepted

## What to build
The ingest **write path** — the system's single state-mutating flow and the first vertical slice of
the RAG pipeline. An analyst uploads a land/title PDF and the API turns it into stored, queryable
state: it parses the PDF text, extracts the document's key structured fields, splits the text into
chunks, embeds each chunk into a vector, and stores chunks + vectors in the in-memory store.

The demo-facing capability: `POST /documents` accepts a PDF and returns the new document id, its
extracted fields, and the number of chunks that were embedded and stored — proving the
`ingest → extract → chunk → embed → store` loop end to end without any read/retrieval surface yet.

Two collaborators do the intelligence work behind ports: field extraction goes through the
**`IChatClient`** port (config-selected; Azure OpenAI GPT live per ADR-0012 — the `Extraction` module's documented LLM call),
and chunk embedding goes through the **`IEmbeddingClient`** port, which for this slice is
`LocalEmbeddingClient` — a deterministic, dependency-free local embedder. This slice stands up the
backend solution, the `Ingestion` and `Extraction` modules, both ports, and the in-memory store that
a later retrieval/Q&A spec will read.

## Constraints
- **Backend:** ASP.NET Core Web API on **.NET 10 (LTS)** under `/backend` (ADR-0003), modular
  monolith (ADR-0004). C# conventions per `CLAUDE.md`: nullable enabled, `async`/`await` end-to-end,
  constructor DI, file-scoped namespaces, one public type per file, `record` DTOs, validate/throw
  early.
- **Endpoint:** `POST /documents`, `multipart/form-data` with a single `file` part (one PDF per
  request). Response `201` with body:
  ```json
  {
    "id": "guid",
    "fileName": "synthetic-lease-01.pdf",
    "status": "ready",
    "fields": [ { "name": "Royalty", "value": "3/16", "sourceChunkId": "guid|null" } ],
    "chunkCount": 7
  }
  ```
  This **adds `chunkCount`** to the `API.md` sketch — reconcile that doc on merge.
- **Extracted fields:** lessor, lessee, legal description, royalty, and key dates *(assumption: dates
  surface as one or more named fields, e.g. `EffectiveDate` / `Term`, rather than a fixed sub-schema —
  the extractor returns named `ExtractedField`s; `sourceChunkId` may be null if the LLM doesn't pin a
  field to a chunk)*.
- **Extraction port:** `IChatClient`, config-selected (`ModelClient:ChatProvider`); the live provider is
  **Azure OpenAI GPT** ([ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md),
  supersedes ADR-0007/0010 — *decided; impl pending*), Anthropic-direct as the config-swap fallback.
  Model id from config, never hardcoded (extraction may use a cheaper model per `CLAUDE.md`). The
  acceptance test injects a **fake `IChatClient`** returning canned fields, so the test is
  deterministic and offline. The chat-provider **availability auto-failover is OUT of scope for this
  spec** — only the config-selected adapter is wired here.
- **Extraction is best-effort (amendment, 2026-06-07):** field extraction is best-effort — ingest
  stores the chunks and returns `201` with an empty `fields` array if the provider can't extract,
  never `500`. Extraction is decoupled from the chunk→embed→store path so a missing key or unreachable
  gateway can't fail the write path (degradation, distinct from the out-of-scope provider failover).
- **Embedding port:** `IEmbeddingClient` = `LocalEmbeddingClient`, a **deterministic hashing /
  bag-of-words** embedder producing a fixed-dimension `float[]` *(assumption: dimension is a small
  constant, e.g. 256, set in config; same text → same vector)*. No model download, no cloud
  dependency (resolves the PRD "local embedding model" open question toward the simplest slice). All
  vectors in the store share one dimension (cosine invariant, DATA-MODEL).
- **PDF parsing:** local text extraction from a **text-based (digital) PDF** *(assumption: a NuGet
  such as UglyToad.PdfPig)*. **No OCR** of scanned/handwritten documents (PRD non-goal); Azure AI
  Document Intelligence OCR tuning stays out of scope.
- **Chunking:** fixed-size character windows with small overlap *(assumption: ~1,000 chars, ~150
  overlap — tune so `synthetic-lease-01.pdf` yields N > 1 chunks)*.
- **Store:** in-memory, process-lifetime only, registered as a shared singleton so a later retrieval
  spec reads the same instance (ADR-0005). No durable persistence (PRD non-goal).
- **Stored chunk contract (the 0001→0002 seam):** each stored `Chunk` is `{ Id, DocumentId, Text,
  Vector }` — a stable `Id`, the owning `DocumentId`, the **source `Text`** it was chunked from, and
  its embedding `Vector`. The read path ([[knowledge/docs/specs/0002-rag-qa-with-citations]]) resolves
  `chunkId → { documentId, text }` from the store to build citations, so dropping `Text` or using
  unstable ids silently breaks 0002's citations. This shape is part of the **write-side** contract,
  not an 0002 concern.
- **Errors:** ASP.NET Core `ProblemDetails` (RFC 7807): `400` for a missing/empty/non-PDF file.
- **Out of scope for this spec:** `GET /documents/{id}`, retrieval, and `POST /documents/{id}/ask`
  (read path — separate specs); the chat availability fallback; Azure AI Search; auth/RBAC;
  observability; multi-file batch upload.

## How to verify
- **Happy path (integration, `WebApplicationFactory`):** `POST /documents` with the
  `synthetic-lease-01.pdf` fixture (a small text-based lease added under the backend test assets) and
  a **fake `IChatClient`** returns `201` with: a non-empty `id`; `fileName` echoing the upload;
  `status` `"ready"`; a non-empty `fields` array containing lessor, lessee, legal description,
  royalty, and at least one date field; and `chunkCount` = N where **N > 1**.
- **Storage assertion:** after that request, the in-memory store holds exactly **N chunks** for the
  returned document id, each carrying a non-empty `float[]` embedding, and **all N vectors share the
  same length** (cosine invariant).
- **Stored chunk shape (the 0001→0002 seam):** each stored chunk **retains its source `Text`**
  (non-empty) and is **resolvable by a stable `Id`** carrying the correct `DocumentId` — i.e. the full
  `{ Id, DocumentId, Text, Vector }` shape, asserted explicitly so a "vector-only" store that drops
  `Text` can't pass while silently breaking 0002's citations.
- **Deterministic embeddings:** embedding the same chunk text twice yields identical vectors
  (unit test over `LocalEmbeddingClient`).
- **Extraction wiring:** with the fake `IChatClient` returning canned fields, those exact fields
  appear in the response — proving the `Extraction` module calls the port and maps its result.
- **Best-effort extraction (amendment):** with an `IChatClient` whose `ExtractFieldsAsync` throws,
  `POST /documents` still returns `201` with `status` `"ready"`, an **empty `fields`** array, and
  `chunkCount` = N (N > 1); the store holds those N chunks for the returned id (no 500).
- **Bad input:** `POST /documents` with no `file` part, an empty file, or a non-PDF returns `400`
  with a `ProblemDetails` body; nothing is added to the store.
- **Suite green (tdd):** `dotnet build` and `dotnet test` pass; the behaviors above are covered by
  new tests, written test-first.

## Links
- **ADRs:** [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] ·
  [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]] ·
  [[knowledge/docs/decisions/0003-dotnet-10-lts]] ·
  [[knowledge/docs/decisions/0004-modular-monolith-over-microservices]] ·
  [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]] (fallback
  deferred — not built here).
- **Docs to reconcile on merge:** `API.md` (add `chunkCount` to `POST /documents`) ·
  `DATA-FLOW.md` (ingest sequence) · `DATA-MODEL.md` (Document / Chunk / ExtractedField) ·
  `ARCHITECTURE.md` (Ingestion + Extraction modules, both ports, in-memory store). Resolves PRD open
  questions on the field set and the local embedding model.
- **Decision pinned in:** [[knowledge/docs/decisions/0008-deterministic-hashing-embeddings-for-slice]]
  — records `LocalEmbeddingClient`'s deterministic-hashing embedding for the slice.
- **Implementing PR:** _TBD — link once opened._
