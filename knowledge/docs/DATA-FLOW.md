# Data flow

Two critical flows: **ingest** (upload → extracted fields, the only state mutation) and **ask**
(question → cited answer, read-only over the store).

## Ingest — upload a document

```mermaid
sequenceDiagram
    actor A as Analyst
    participant SPA as React SPA
    participant API as Web API (Ingestion)
    participant E as IEmbeddingClient
    participant V as Vector store
    participant C as IChatClient

    A->>SPA: Upload PDF
    SPA->>API: POST /documents (PDF)
    API->>API: Parse text
    API->>C: Extract fields (Extraction, IChatClient)
    C-->>API: Structured fields
    API->>API: Chunk text
    API->>E: Embed chunks (IEmbeddingClient)
    E-->>API: Vectors
    API->>V: Store chunks + vectors
    API-->>SPA: documentId + fields + chunkCount
    SPA-->>A: Show extracted fields
```

**State change:** ingest is the only write — chunks + vectors are added to the in-memory store, and
the document's extracted fields are produced.

## Ask — question with citations

```mermaid
sequenceDiagram
    actor A as Analyst
    participant SPA as React SPA
    participant API as Web API (Qa)
    participant E as IEmbeddingClient
    participant V as Vector store
    participant C as IChatClient

    A->>SPA: Ask a question
    SPA->>API: POST /ask { question }
    API->>E: Embed question (IEmbeddingClient)
    E-->>API: Query vector
    API->>V: Top-k by cosine (all ingested docs)
    note over V: empty store → 409
    V-->>API: Relevant chunks
    API->>C: Answer grounded in chunks (IChatClient)
    note over C: Foundry primary (config-selected adapter);<br/>ADR-0007 Anthropic fallback deferred
    C-->>API: Answer + citations
    API-->>SPA: Answer + citations (≥1, each resolves)
    SPA-->>A: Cited answer
```

**State change:** none — ask is read-only. The retrieved chunks become the answer's grounding, and
the cited chunk IDs are returned so the UI can resolve each claim to its source text.

## Notes
- The same `IEmbeddingClient` embeds chunks at ingest and the query at ask — they must share a model
  (and thus dimension) for cosine similarity to be meaningful (`LocalEmbeddingClient`, deterministic
  hashing, for the slice — see [ADR-0008](decisions/0008-deterministic-hashing-embeddings-for-slice.md)).
- Retrieval is **global** — top-k across all ingested documents (see
  [ADR-0009](decisions/0009-corpus-wide-ask-retrieval-scope.md)); each citation carries `documentId`.
  An answer is never returned without ≥1 citation; an empty store returns `409`
  ([spec 0002](specs/0002-rag-qa-with-citations.md)).
- Repeated document context sent to the chat model is intended to rely on **prompt caching** to cut
  cost/latency — an optimization **deferred** for the slice (not required by spec 0002).
- Chat is designed **Foundry-primary with Anthropic-direct fallback** on availability failures
  ([ADR-0007](decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md)); the slice wires
  only the config-selected adapter — the **failover wrapper is deferred** to its own spec.
