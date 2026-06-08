# Data model

The slice is **in-memory** — these are domain types (C# `record`s), not database tables. They model
the RAG pipeline's state: a `Document` is split into `Chunk`s (each carrying an embedding vector) and
yields `ExtractedField`s; an `Answer` is supported by `Citation`s that resolve back to chunks.

```mermaid
erDiagram
    DOCUMENT ||--o{ CHUNK : "split into"
    DOCUMENT ||--o{ EXTRACTED_FIELD : "yields"
    CHUNK ||--o{ CITATION : "cited by"
    CHUNK ||--o{ SCORED_CHUNK : "scored at retrieval"
    CHUNK ||--o{ QA_PASSAGE : "projected for chat"
    ANSWER ||--o{ CITATION : "supported by"

    DOCUMENT {
      Guid Id
      string FileName
      string Status
    }
    CHUNK {
      Guid Id
      Guid DocumentId
      string Text
      vector Vector "float[] cosine vector"
      string Source "sanitized source-doc name"
    }
    EXTRACTED_FIELD {
      string Name
      string Value
      Guid SourceChunkId "nullable"
    }
    ANSWER {
      Guid Id
      string Question
      string Text
    }
    CITATION {
      Guid ChunkId
      Guid DocumentId
      double Score
      string Text "resolved from store"
    }
    SCORED_CHUNK {
      double Score "cosine; wraps a Chunk"
    }
    QA_PASSAGE {
      Guid ChunkId
      Guid DocumentId
      string Text
      string SourceName
    }
```

## Entities
- **Document** — an uploaded document (PDF, text, or Markdown) and its ingest status (`Status` is `"ready"` once ingested). There is
  no stored `Document` record in the slice: a document is identified by a generated `documentId` and
  surfaced through the `POST /documents` response (`id`, `fileName`, `status`, the extracted `fields`,
  and a **derived** `chunkCount` — the count of the document's stored chunks, not a stored field).
- **Chunk** — a contiguous slice of a document's text, its embedding vector, and `Source` — the
  sanitized source-document name (newlines/brackets neutralized) used to label the chunk in the grounding
  prompt (ADR-0014).
- **ExtractedField** — a structured field pulled from the document (e.g. royalty, lessor), with the
  chunk it came from (`SourceChunkId` may be null when a field isn't pinned to a chunk).
- **Answer** — a generated response to a question over the **whole corpus** (global `/ask`, ADR-0009),
  not scoped to one document — its supporting `Citation`s each carry a `DocumentId`. Conceptual: the
  slice stores no `Answer` record (the `/ask` response is `answer` + `citations[]`).
- **Citation** — a pointer from an answer (or extracted field) to the chunk that supports it (carries
  `ChunkId` + `DocumentId` + `Score`). The `POST /ask` response DTO additionally inlines the chunk
  `text` (resolved from the store) so the UI can show the source without a second call.
- **ScoredChunk** — a retrieved `Chunk` paired with its cosine `Score`; the `IVectorStore.TopKAsync`
  result. Transient — not stored.
- **QaPassage** — the chat-context projection of a retrieved chunk (`ChunkId`, `DocumentId`, `Text`,
  `SourceName`) passed to `IChatClient.AnswerAsync`; `SourceName` labels each passage by source document
  so cross-document answers can disambiguate (ADR-0014). Keeps the chat port free of `Storage` types
  (ADR-0002 / ADR-0004). Transient — not stored.

## Invariants
- Every `Chunk` belongs to exactly one `Document`.
- All embeddings within a store share one dimension (cosine similarity requires equal length).
- Every `Answer` carries **≥ 1** `Citation`, and every `Citation.ChunkId` resolves to a stored `Chunk`.
- `ExtractedField.SourceChunkId` (when set) resolves to a stored `Chunk`.

## Indexes / migrations
- **Live default — Azure AI Search Free tier** (`landdoc-chunks` index): key = chunk id; fields
  `documentId`, `text`, `source`; a 256-d vector field (HNSW + cosine) sized to `Embedding:Dimension`.
  Chunks **persist across restarts/redeploys**; the adapter ensures the index exists on startup
  (idempotent). No relational schema — the index is the store. See
  [ADR-0017](decisions/0017-azure-ai-search-free-tier-live-vector-store.md).
- **Re-ingest, not migrate:** an embedding model/dimension change invalidates the index (the 256-d
  field is fixed at create) → re-ingest. Keep the same embedder for index and query (cosine invariant).
- **Offline/test** — `InMemoryVectorStore`, a cosine-scanned in-process collection (no persistence),
  selected via `VectorStore:Provider=inmemory` (see
  [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md)).
