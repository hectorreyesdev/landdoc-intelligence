# 0002 — RAG Q&A with Citations (Read Path)

**Status:** Accepted

## What to build
The retrieval-augmented **read path** — the complement to the ingest write path
([[knowledge/docs/specs/0001-document-ingestion-write-path]]) and the half of the slice that closes
the loop. An analyst asks a free-text question and gets back a grounded answer plus citations that
point at the exact source chunks the answer came from.

The demo-facing capability: `POST /ask` accepts `{ question }`, embeds the query with the **same**
`IEmbeddingClient` used at ingest, retrieves the top-k most similar chunks across everything in the
in-memory store by cosine similarity, hands those chunks + the question to the `IChatClient` to
compose an answer grounded only in them, and returns `{ answer, citations[] }` where every citation
resolves back to a stored chunk. This is a **read-only** flow — it never mutates the store.

Two modules do the work behind the existing ports: `Retrieval` (embed query → top-k from the store)
and `Qa` (retrieved chunks + question → cited answer via `IChatClient`). It reads the same shared
in-memory store that spec 0001 populates, so this spec **depends on 0001** being in place.

## Constraints
- **Backend:** ASP.NET Core Web API on .NET 10 under `/backend`, modular monolith (ADR-0004); C#
  conventions per `CLAUDE.md` (nullable, async end-to-end, constructor DI, file-scoped namespaces,
  `record` DTOs, validate/throw early).
- **Endpoint:** `POST /ask`, `application/json`. Request `{ "question": "..." }`. Response `200`:
  ```json
  {
    "answer": "The lessee is Acme Minerals LLC.",
    "citations": [
      { "chunkId": "guid", "documentId": "guid", "score": 0.82, "text": "…by and between … as Lessee …" }
    ]
  }
  ```
  Retrieval is **global** — top-k across ALL ingested documents in the store; each citation carries
  `documentId` so the UI can resolve which document a claim came from. This **supersedes** the
  `POST /documents/{id}/ask` shape sketched in `API.md`/`DATA-FLOW.md` (reconcile both on merge).
- **Ports:** `IEmbeddingClient` embeds the query (must be the **same** adapter/model that embedded the
  chunks — `LocalEmbeddingClient` deterministic hashing for the slice — so vectors share a dimension,
  the cosine invariant in DATA-MODEL). `IChatClient` composes the answer, config-selected adapter
  (Foundry primary, model id from config — default `claude-opus-4-8`). The acceptance test injects a
  **fake `IChatClient`** so the test is deterministic and offline.
- **Retrieval:** top-k by cosine similarity via linear scan over the in-memory store (ADR-0005),
  through the narrow store seam that ADR-0005 calls for *(assumption: a small `IVectorStore`-style
  read method `TopK(queryVector, k)`; introduce it here if 0001 didn't)*. *(assumption: `k = 5`,
  configurable via `Retrieval:TopK`; deterministic stable tie-break by chunk index/id so a fixed query
  yields a fixed ordering.)*
- **Grounding + citations:** the `Qa` prompt instructs the model to answer **only** from the supplied
  chunks and to ground every claim; the returned `citations[]` are the retrieved chunks (chunkId,
  documentId, cosine score, and the chunk `text` resolved from the store).
- **No-grounding behavior (strict cite-or-error):** an **empty store** (no documents ingested) →
  `409 ProblemDetails`. When the store is non-empty, the response **always** carries ≥1 citation (the
  top-k retrieved chunks); if the answer isn't supported by them, the model replies "not found in the
  document(s)" but the citations still show what was searched. **An answer is never returned without a
  citation** — upholds the core invariant (DATA-MODEL / ARCHITECTURE).
- **Errors:** `ProblemDetails` (RFC 7807) — `400` for a missing/empty/whitespace `question`; `409`
  for an empty store.
- **Out of scope for this spec:** the Foundry→Anthropic availability **failover** (ADR-0007 — its own
  later spec; only the config-selected adapter is wired here) · prompt-caching optimization *(noted in
  DATA-FLOW; not required for acceptance)* · Azure AI Search · reranking/ANN indexing · multi-turn
  conversation history · streaming responses · auth/RBAC · observability.

## How to verify
- **Happy path (integration, `WebApplicationFactory`, fake `IChatClient`):** with
  `synthetic-lease-01.pdf` ingested via the 0001 write path, `POST /ask { "question": "Who is the
  lessee?" }` returns `200` with a non-empty `answer` and a non-empty `citations[]`; **every**
  `citation.chunkId` resolves to a stored chunk and each citation includes `documentId`, a numeric
  `score`, and non-empty `text`.
- **Retrieval correctness:** the chunk that actually contains the lessee clause is present in the
  returned top-k (proves embedding-the-query → cosine top-k surfaces the right source). The fake
  `IChatClient` receives those chunks in its prompt and returns a canned answer + a citation
  referencing a real retrieved `chunkId`.
- **Live-demo correctness (manual, real adapter):** the same question against the real config-selected
  `IChatClient` returns the **actual lessee name** from the document, with the citation pointing at
  the source chunk. (Offline tests assert the contract + retrieval; the real name is a live check.)
- **Deterministic retrieval:** the same question over the same store yields the same top-k chunk ids
  in the same order (hashing embeddings + stable tie-break).
- **No-grounding — empty store:** `POST /ask` against an **empty store** returns `409 ProblemDetails`;
  no `200`/no answer-without-citation is ever produced.
- **No-grounding — out-of-corpus question (anti-hallucination, the trust beat):** with the fixture
  ingested (store **populated**), `POST /ask` with an **out-of-corpus** question (one whose answer is
  not in any chunk — e.g. `"What is the offshore platform's water depth?"` against a land lease)
  returns `200` where the `answer` **signals not-found** (does not fabricate) **and** `citations[]` is
  **still ≥1** — the top-k chunks that were searched — each resolving to a stored chunk. This is the
  named test (e.g. `Ask_OutOfCorpusQuestion_AnswerSignalsNotFound_AndStillCites`), not prose.
  - *Contract half (offline, fake `IChatClient`):* the fake returns a canned "not found in the
    document(s)" answer; assert the response carries that answer **and** ≥1 resolving citation. The
    assertion keys off the fake's exact output, not fuzzy NL parsing *(if we later want a
    machine-checkable signal, add a `grounded: false` flag — a separate contract change, out of scope
    here)*.
  - *Real-model half (manual, live, real adapter):* given only the retrieved chunks, the real model
    **admits it can't answer** rather than hallucinating, with citations showing where it looked —
    same offline-contract / live-correctness split used for the lessee check above.
- **Bad input:** missing / empty / whitespace `question` → `400 ProblemDetails`.
- **Read-only:** the store contents (chunk count, vectors) are unchanged after any number of `/ask`
  calls.
- **Suite green (tdd):** `dotnet build` and `dotnet test` pass; all behaviors above are covered by new
  tests, written test-first.

## Links
- **Depends on:** [[knowledge/docs/specs/0001-document-ingestion-write-path]] (populates the shared
  in-memory store this path reads).
- **ADRs:** [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]
  (in-memory top-k cosine + store seam) ·
  [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] (both ports) ·
  [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]] (failover —
  **deferred**, not built here) · [[knowledge/docs/decisions/0004-modular-monolith-over-microservices]] ·
  [[knowledge/docs/decisions/0003-dotnet-10-lts]].
- **Docs to reconcile on merge:** `API.md` (replace `POST /documents/{id}/ask` with global
  `POST /ask`; citation gains `documentId`) · `DATA-FLOW.md` (ask sequence → global, no doc id) ·
  `DATA-MODEL.md` (confirm Citation carries `DocumentId`; API citation DTO also returns chunk `text`) ·
  `ARCHITECTURE.md` (Retrieval + Qa modules over the store seam). Resolves the PRD open question on
  one-document-vs-corpus toward a global corpus query.
- **Implementing PR:** _TBD — link once opened._
