# 0009. Corpus-wide retrieval scope for the ask endpoint (`POST /ask`)

- Status: Accepted
- Date: 2026-06-06

## Context
The read path ([[knowledge/docs/specs/0002-rag-qa-with-citations]]) answers questions over the
in-memory store. The original `API.md` / `DATA-FLOW.md` sketch exposed `POST /documents/{id}/ask` —
a question **scoped to one document**. Spec 0002 instead specified `POST /ask` with body
`{ question }` and **no document id**, which forces an explicit decision about retrieval scope.

Forces at play:
- **The store is one global collection.** The in-memory vector store
  ([[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]) holds
  chunks across **all** ingested documents; a single linear cosine scan over everything is the
  natural query, and the corpus is tiny enough that scanning all of it is cheap.
- **Citations already identify the source.** A `Citation` carries `DocumentId` (`DATA-MODEL.md`), so
  a cross-document answer can still point each claim back to the right file — global retrieval does
  not lose traceability.
- **Demo simplicity.** Asking without first selecting a document is the simpler, more compelling
  demo beat; `PRD.md` carried this as the open question *"one document at a time, or a small corpus
  per session?"*
- **No prior ADR to supersede.** The per-document shape lived only in a design-doc sketch, never an
  Accepted ADR; `API.md` / `DATA-FLOW.md` were realigned to `/ask` this session.

Builds on [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]];
relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]].

## Decision
We will make **`POST /ask` a corpus-wide query**: the request body is `{ question }` with **no
document id**, and retrieval returns top-k by cosine across **all** chunks in the in-memory store
regardless of source document. Each `Citation` carries `DocumentId` so the UI can resolve which
document a cited chunk came from. This **supersedes the `POST /documents/{id}/ask` shape** sketched
in `API.md` / `DATA-FLOW.md` (now realigned). The grounding/strict-cite-or-error behavior is governed
by spec 0002 and is not re-decided here. Binding on the slice's read path; it does **not** change
`IChatClient` / `IEmbeddingClient`.

## Consequences
- **Simpler UX / demo.** The analyst asks a question without first picking a document; the system
  finds the relevant source(s) across everything ingested.
- **Fits the store shape.** Matches the single global collection and the tiny-corpus linear scan
  (ADR-0005) — no per-document partitioning needed.
- **Traceability preserved.** Citations carrying `DocumentId` keep cross-document answers attributable
  to the right file.
- **Tradeoff — no document filter.** A question can't be scoped to one document; on a large or
  heterogeneous corpus this could surface cross-document noise or blend sources in one answer.
  Acceptable for the tiny demo corpus.
- **Resolves** the PRD "one document vs. corpus" open question toward corpus-wide.
- **Production carry-forward.** The Azure AI Search path would implement the same global query over a
  real index — the scope decision carries forward; only the mechanism changes (ADR-0005).
- **Follow-on.** If document-scoped queries are later needed, add an optional `documentId` filter (new
  spec/ADR); `GET /documents/{id}` (read-back) remains intended-but-unspecced.
