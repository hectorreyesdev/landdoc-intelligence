# API

> **Partly built.** `POST /documents` (spec 0001 — ingest write path) and `POST /ask` (spec 0002 —
> read path) are both implemented. `GET /documents/{id}` and a list endpoint remain intended surface.
> The shapes below are the accepted-spec contracts.

Base path: none — endpoints are served at the root (`/documents`, `/ask`), matching specs 0001/0002.
The SPA reaches them **same-origin** via relative paths (dev: Vite dev-proxy; prod: Azure Static Web
Apps linked backend — no CORS; see [ADR-0011](decisions/0011-single-origin-spa-api-topology.md)).
Media type: `application/json`, except upload (`multipart/form-data`).

## Endpoints (proposed)

### `POST /documents`  ·  spec [0001](specs/0001-document-ingestion-write-path.md)
Upload and ingest a PDF (parse → extract fields → chunk → embed → store).
- Request: `multipart/form-data` with a single `file` part (one text-based PDF; no OCR).
- Response `201`:
  ```json
  {
    "id": "guid",
    "fileName": "lease.pdf",
    "status": "ready",
    "fields": [
      { "name": "Royalty", "value": "3/16", "sourceChunkId": "guid" }
    ],
    "chunkCount": 7
  }
  ```
  `sourceChunkId` may be `null` when a field isn't pinned to a chunk. Field extraction is **best-effort**
  (spec 0001 amendment): if the chat provider can't extract, the document is still chunked, embedded, and
  stored — the response is `201` with an empty `fields` array (`chunkCount` unaffected). `400` on a
  missing/empty/non-PDF file.

### `GET /documents/{id}`  ·  *intended — not yet specced*
Fetch a document and its extracted fields.
- Response `200`: same shape as `POST /documents` · `404` if unknown.
- The accepted slice specs (0001 write path, 0002 read path) do **not** build this read-back endpoint;
  it remains intended surface for a later spec.

### `POST /ask`  ·  spec [0002](specs/0002-rag-qa-with-citations.md)
Ask a question, grounded in chunks retrieved across **all** ingested documents (global corpus query —
see [ADR-0009](decisions/0009-corpus-wide-ask-retrieval-scope.md)).
- Request:
  ```json
  { "question": "Who is the lessee?" }
  ```
- Response `200`:
  ```json
  {
    "answer": "The lessee is Acme Minerals LLC.",
    "citations": [
      { "chunkId": "guid", "documentId": "guid", "score": 0.82, "text": "…by and between … as Lessee …" }
    ]
  }
  ```
  Strict cite-or-error invariant: an answer is **never** returned without ≥1 citation, and each
  `chunkId` resolves to a stored chunk (`documentId` tells the UI which document). An **empty store**
  (nothing ingested) → `409`; a blank `question` → `400`. Read-only — `/ask` never mutates the store.

> TODO: confirm whether a `GET /documents` list endpoint is needed for the slice.

## Error model
Standard ASP.NET Core **`ProblemDetails`** (RFC 7807):
- `400` — validation (missing/empty/non-PDF file; blank question).
- `404` — unknown document id (`GET /documents/{id}`).
- `409` — `POST /ask` against an empty store (nothing ingested to cite).
- `502` / `503` — a provider failure that **isn't** swallowed: the *embedding* provider failing at
  ingest, or the *chat* provider failing at `/ask`. A *chat*-provider failure during **ingest** is not an
  error — field extraction is best-effort (`201` with empty `fields`; spec 0001 amendment). (Chat provider
  is config-selected — Azure OpenAI GPT live, Anthropic-direct fallback —
  [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md); an availability
  auto-failover is deferred.)
