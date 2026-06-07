# API

> **Intended surface — not built yet.** No endpoints exist; this sketches the contract the SPA will
> consume. Shapes are proposals; update as the API is implemented.

Base path: `/api` (TODO confirm). Media type: `application/json`, except upload (`multipart/form-data`).

## Endpoints (proposed)

### `POST /documents`
Upload and ingest a PDF (parse → chunk → embed → store → extract fields).
- Request: `multipart/form-data` with a `file` part (PDF).
- Response `201`:
  ```json
  {
    "id": "guid",
    "fileName": "lease.pdf",
    "status": "ready",
    "fields": [
      { "name": "Royalty", "value": "3/16", "sourceChunkId": "guid" }
    ]
  }
  ```

### `GET /documents/{id}`
Fetch a document and its extracted fields.
- Response `200`: same shape as above · `404` if unknown.

### `POST /documents/{id}/ask`
Ask a question, grounded in the document's chunks.
- Request:
  ```json
  { "question": "What is the royalty rate?" }
  ```
- Response `200`:
  ```json
  {
    "answer": "The royalty is 3/16.",
    "citations": [
      { "chunkId": "guid", "score": 0.82, "text": "…reserving a royalty of 3/16…" }
    ]
  }
  ```
  Invariant: `citations` is non-empty and each `chunkId` resolves to a stored chunk.

> TODO: confirm whether a `GET /documents` list endpoint is needed for the slice.

## Error model
Standard ASP.NET Core **`ProblemDetails`** (RFC 7807):
- `400` — validation (missing file, empty question).
- `404` — unknown document id.
- `502` / `503` — chat/embedding provider failed (after Foundry → Anthropic fallback for chat).
