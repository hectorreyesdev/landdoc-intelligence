# 0001 — Document Ingestion (Write Path)

**Status:** Accepted · _Amended 2026-06-08 — extraction is doc-type-agnostic (generic role-neutral schema, ADR-0015)._

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
and chunk embedding goes through the **`IEmbeddingClient`** port (config-selected; Azure OpenAI
`text-embedding-3-small` live per ADR-0013, with `LocalEmbeddingClient` hashing as the offline/test
embedder). This slice stands up the
backend solution, the `Ingestion` and `Extraction` modules, both ports, and the in-memory store that
a later retrieval/Q&A spec will read.

## Constraints
- **Backend:** ASP.NET Core Web API on **.NET 10 (LTS)** under `/backend` (ADR-0003), modular
  monolith (ADR-0004). C# conventions per `CLAUDE.md`: nullable enabled, `async`/`await` end-to-end,
  constructor DI, file-scoped namespaces, one public type per file, `record` DTOs, validate/throw
  early.
- **Endpoint:** `POST /documents`, `multipart/form-data` with a single `file` part (one file per
  request — PDF originally; `.txt`/`.md`/`.markdown` added by spec 0005, see amendment). Response `201` with body:
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
- **Extracted fields (amended 2026-06-08 — doc-type-agnostic, ADR-0015):** the corpus spans many
  instrument types (leases, deeds, orders, opinions, agreements, AFE, affidavit…), so extraction uses a
  **generic role-neutral schema**, not OGL-specific names: a **universal core** (`DocumentType`;
  `Parties` as role-labeled `{role, name}` — Lessor/Lessee, Grantor/Grantee, Operator, Assignor/Assignee,
  Affiant, Heirs…; `EffectiveDate`; `LegalDescription`; `County`; `State`), **conditional economics**
  emitted only when present (`Acres`, `Royalty`, `Bonus`, `PrimaryTerm`), and an open **`OtherNotableTerms`**
  list for type-specific terms. The extractor returns named `ExtractedField`s (each party flattens to one
  field whose name is its role); `sourceChunkId` is null (extraction runs on full text before chunking).
  See ADR-0015.
- **Extraction port:** `IChatClient`, config-selected (`ModelClient:ChatProvider`); the live provider is
  **Azure OpenAI GPT** ([ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md),
  supersedes ADR-0007/0010), Anthropic-direct as the config-swap fallback.
  Model id from config, never hardcoded (extraction may use a cheaper model per `CLAUDE.md`). The
  acceptance test injects a **fake `IChatClient`** returning canned fields, so the test is
  deterministic and offline. The chat-provider **availability auto-failover is OUT of scope for this
  spec** — only the config-selected adapter is wired here.
- **Extraction is best-effort (amendment, 2026-06-07):** field extraction is best-effort — ingest
  stores the chunks and returns `201` with an empty `fields` array if the provider can't extract,
  never `500`. Extraction is decoupled from the chunk→embed→store path so a missing key or unreachable
  gateway can't fail the write path (degradation, distinct from the out-of-scope provider failover).
- **Embedding port:** `IEmbeddingClient` is config-selected via `ModelClient:EmbeddingProvider`; the
  live slice default = Azure OpenAI `text-embedding-3-small` (`AzureOpenAIEmbeddingClient`), with
  `LocalEmbeddingClient` (**deterministic hashing / bag-of-words**) as the offline/test embedder
  (ADR-0013, supersedes ADR-0008). Either way it produces a fixed-dimension `float[]` *(assumption:
  dimension is a small constant, e.g. 256, set in config; for the local embedder, same text → same
  vector)*. All vectors in the store share one dimension (cosine invariant, DATA-MODEL).
- **PDF parsing:** local text extraction from a **text-based (digital) PDF** *(assumption: a NuGet
  such as UglyToad.PdfPig)*. **No OCR** of scanned/handwritten documents (PRD non-goal); Azure AI
  Document Intelligence OCR tuning stays out of scope.
- **Chunking:** fixed-size character windows with small overlap *(assumption: ~1,000 chars, ~150
  overlap — tune so `synthetic-lease-01.pdf` yields N > 1 chunks)*.
- **Store:** in-memory, process-lifetime only, registered as a shared singleton so a later retrieval
  spec reads the same instance (ADR-0005). No durable persistence (PRD non-goal).
- **Stored chunk contract (the 0001→0002 seam):** each stored `Chunk` is `{ Id, DocumentId, Text,
  Vector, Source }` — a stable `Id`, the owning `DocumentId`, the **source `Text`** it was chunked from,
  its embedding `Vector`, and `Source` (the sanitized source-document name for grounding labels — added by
  ADR-0014). The read path ([[knowledge/docs/specs/0002-rag-qa-with-citations]]) resolves
  `chunkId → { documentId, text }` from the store to build citations, so dropping `Text` or using
  unstable ids silently breaks 0002's citations. This shape is part of the **write-side** contract,
  not an 0002 concern.
- **Errors:** ASP.NET Core `ProblemDetails` (RFC 7807): `400` for a missing/empty file or an
  unsupported file type *(spec 0005 amendment, below, supersedes the original "non-PDF → 400")*.
- **Accepted formats extended → spec 0005 (amendment, 2026-06-08):** `POST /documents` now also accepts
  `.txt`, `.md`, and `.markdown` uploads alongside PDF, selected by **filename extension** — see
  [[knowledge/docs/specs/0005-ingest-markdown-and-text-documents]] (Accepted, merged PR #19). Text/markdown
  bytes are UTF-8-decoded (no parsing) and flow through the same chunk→embed→store + best-effort extraction
  path; a missing/empty file, an unsupported extension, or a `.pdf` failing the `%PDF-` guard returns `400`.
  Ports and the `Chunk` contract are unchanged.
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
  `{ Id, DocumentId, Text, Vector, Source }` shape (`Source` = the sanitized source-document name, added by
  ADR-0014), asserted explicitly so a "vector-only" store that drops `Text` can't pass while silently
  breaking 0002's citations.
- **Deterministic embeddings:** tests pin `EmbeddingProvider=local`, so embedding the same chunk text
  twice yields identical vectors (unit test over `LocalEmbeddingClient`, the offline/test embedder per
  ADR-0013).
- **Extraction wiring:** with the fake `IChatClient` returning canned fields, those exact fields
  appear in the response — proving the `Extraction` module calls the port and maps its result.
- **Best-effort extraction (amendment):** with an `IChatClient` whose `ExtractFieldsAsync` throws,
  `POST /documents` still returns `201` with `status` `"ready"`, an **empty `fields`** array, and
  `chunkCount` = N (N > 1); the store holds those N chunks for the returned id (no 500).
- **Bad input:** `POST /documents` with no `file` part, an empty file, or an unsupported file type
  (per the spec 0005 amendment — an unknown extension, or a `.pdf` failing the `%PDF-` guard) returns
  `400` with a `ProblemDetails` body; nothing is added to the store.
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
- **Decision pinned in:** [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]
  — live slice default = Azure OpenAI `text-embedding-3-small` (`AzureOpenAIEmbeddingClient`), config-selected
  via `ModelClient:EmbeddingProvider`, with `LocalEmbeddingClient` hashing as the offline/test embedder;
  supersedes [[knowledge/docs/decisions/0008-deterministic-hashing-embeddings-for-slice]].
- **Implementing PR:** _TBD — link once opened._
