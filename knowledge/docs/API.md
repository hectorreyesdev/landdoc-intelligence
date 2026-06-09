# API

> **Built.** `POST /documents` (spec 0001 — ingest write path), `POST /ask` (spec 0002 — read path), the
> document read-back surface `GET /documents`, `GET /documents/{id}`, `GET /documents/{id}/file` (spec 0006),
> and `DELETE /documents/{id}` (spec 0008) are all implemented. The shapes below are the accepted-spec
> contracts.

Base path: none — endpoints are served at the root (`/documents`, `/ask`), matching specs 0001/0002.
The SPA reaches them **same-origin** via relative paths (dev: Vite dev-proxy; prod: one container serving
the SPA + API on a single origin — no CORS; see [ADR-0011](decisions/0011-single-origin-spa-api-topology.md)
and [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md)).
Media type: `application/json`, except upload (`multipart/form-data`).

## Endpoints (proposed)

### `POST /documents`  ·  spec [0001](specs/0001-document-ingestion-write-path.md), extended by [0005](specs/0005-ingest-markdown-and-text-documents.md)
Upload and ingest a document — a PDF or a text/markdown file (parse **or** UTF-8-decode → extract fields → chunk → embed → store).
- Request: `multipart/form-data` with a single `file` part. The format is selected by **filename extension**: `.pdf` (text-based, no OCR), or `.txt` / `.md` / `.markdown` (read as UTF-8 — the bytes are the document text, no parsing).
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
  missing/empty file, an unsupported extension, or a `.pdf` whose bytes fail the `%PDF-` magic-byte check.

### `GET /documents`  ·  spec [0006](specs/0006-document-read-back-list-view-original-file.md)
List every ingested document's metadata + extracted fields (persisted; survives restart — ADR-0018).
- Response `200`: an array of document metadata (empty array `[]` when nothing is ingested — **not** 404):
  ```json
  [
    {
      "id": "guid",
      "fileName": "lease.pdf",
      "status": "ready",
      "contentType": "application/pdf",
      "chunkCount": 7,
      "fields": [ { "name": "Royalty", "value": "3/16", "sourceChunkId": null } ],
      "ingestedAt": "2026-06-08T12:00:00+00:00"
    }
  ]
  ```

### `GET /documents/{id}`  ·  spec [0006](specs/0006-document-read-back-list-view-original-file.md)
Fetch one document's metadata + extracted fields.
- Response `200`: a single document-metadata object (same shape as a `GET /documents` element) · `404` if unknown.

### `GET /documents/{id}/file`  ·  spec [0006](specs/0006-document-read-back-list-view-original-file.md)
Fetch a document's **original uploaded file** (ADR-0018), served **inline** with its stored `Content-Type`
so the SPA embeds it in an `<iframe>`.
- Response `200`: the raw file bytes (`application/pdf`, `text/plain`, or `text/markdown`) · `404` if unknown.

### `DELETE /documents/{id}`  ·  spec [0008](specs/0008-delete-documents-multi-select.md)
Remove a document completely — its file + metadata (document store) **and** all of its chunks (vector store).
- Response **`204 No Content`**. **Idempotent**: deleting an unknown id is a no-op (still `204`).
- The UI deletes multiple selected documents by calling this once per id.

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
      { "chunkId": "guid", "documentId": "guid", "score": 0.82, "text": "…by and between … as Lessee …", "source": "lease.pdf" }
    ]
  }
  ```
  Strict cite-or-error invariant: an answer is **never** returned without ≥1 citation, and each
  `chunkId` resolves to a stored chunk (`documentId` tells the UI which document; `source` is the file
  name, so the UI labels the citation and links to `GET /documents/{documentId}` —
  [ADR-0014](decisions/0014-surface-source-document-identity-in-ask-grounding-context.md) follow-on,
  spec 0006). An **empty store** (nothing ingested) → `409`; a blank `question` → `400`. Read-only —
  `/ask` never mutates the store.

## Error model
Standard ASP.NET Core **`ProblemDetails`** (RFC 7807):
- `400` — validation (missing/empty file, unsupported file type, or a `.pdf` failing the `%PDF-` magic-byte check; blank question).
- `404` — unknown document id (`GET /documents/{id}`).
- `409` — `POST /ask` against an empty store (nothing ingested to cite).
- `502` / `503` — a provider failure that **isn't** swallowed: the *embedding* provider failing at
  ingest, or the *chat* provider failing at `/ask`. A *chat*-provider failure during **ingest** is not an
  error — field extraction is best-effort (`201` with empty `fields`; spec 0001 amendment). (Chat provider
  is config-selected — Azure OpenAI GPT live, Anthropic-direct fallback —
  [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md); an availability
  auto-failover is deferred.)
