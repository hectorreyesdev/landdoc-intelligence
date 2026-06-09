# 0019. Hard, best-effort, non-transactional document deletion

- Status: Accepted
- Date: 2026-06-09
- Realized by: [spec 0008](../specs/0008-delete-documents-multi-select.md)
- Builds on: [ADR-0017](0017-azure-ai-search-free-tier-live-vector-store.md), [ADR-0018](0018-persisted-document-store-azure-blob-for-original-files-and-metadata.md)

## Context
Spec 0008 adds document deletion. A single logical document is spread across **two independent stores**:
its chunks live in the vector store (Azure AI Search — ADR-0017) and its original file + metadata live in
the document store (Azure Blob — ADR-0018). These are separate services with **no shared transaction**, so
"delete a document" is inherently two operations that can't be committed atomically without extra
machinery (a saga/outbox, two-phase commit, or a compensation log).

`DELETE /documents/{id}` therefore needs a stated consistency posture, across a few axes:
- **Hard vs. soft delete** — remove the data outright, or mark it deleted (trash/undo) and garbage-collect
  later.
- **Atomicity** — guarantee both stores change together, or accept that one can succeed while the other
  fails.
- **Failure semantics** — what the caller sees, and how the system converges after a partial failure.

The slice is explicitly *not* production-hardened (CLAUDE.md), and auth/audit are out of scope, so the bar
is "simple, predictable, and safe to retry," not "transactionally perfect."

## Decision
We will make deletion **hard, best-effort, and non-transactional**, with **idempotency** as the
correctness guarantee:

1. **Hard delete.** `DELETE /documents/{id}` permanently removes the document's chunks
   (`IVectorStore.DeleteByDocumentAsync`) and then its file + metadata (`IDocumentStore.DeleteAsync`).
   No soft-delete flag, trash, or undo.
2. **Best-effort, sequential, non-transactional.** The endpoint deletes chunks, then the document — two
   independent store calls with no surrounding transaction or saga. A failure between them surfaces to the
   caller as `ProblemDetails` and **may leave an orphan** (most likely a document record whose chunks are
   already gone, since chunks are deleted first).
3. **Every operation is idempotent**, so re-issuing the same `DELETE` converges (at-least-once cleanup):
   the Azure AI Search adapter deletes by key after a filter query (deleting already-absent keys is a
   no-op), the Blob adapter uses `DeleteIfExists` on both per-document blobs, and the in-memory adapters
   remove-if-present. The endpoint returns **`204 No Content`** whether or not the id existed.
4. **The frontend relies on this idempotency:** multi-select delete simply loops the per-id `DELETE` and
   reloads; any failed id can be retried safely.

## Consequences
- **Simple — no orchestration infrastructure** (no saga/outbox/2PC, no background GC). Fits the slice.
- **A partial-failure window exists.** If the document delete fails after the chunk delete succeeds, the
  document briefly remains listed/viewable but its `/ask` grounding is gone; re-running the delete clears
  it. The reverse orphan (chunks without a document) is possible too. **Retry is the convergence
  mechanism**, enabled by idempotency.
- **No undo and no audit trail.** A mistaken delete is unrecoverable in the slice. Acceptable here; a
  production system would add soft-delete + retention/GC and an audit record — a future ADR.
- **Idempotent `204`** keeps the client trivial and makes bulk/multi-select delete safe and re-runnable.
- **Production hardening is a config/feature change, not a rewrite:** the same ports could gain a saga or a
  soft-delete state, or deletion could move to an outbox-driven worker, behind the existing seams.

## Notes (non-binding)
- Deletion is exposed **per id**; a single-request bulk endpoint (`POST /documents/delete`) is a noted
  future optimization (spec 0008, out of scope) and would inherit this same best-effort/idempotent posture.
- The Azure AI Search adapter deletes a document's chunks via one `Size`-bounded filter query; paging is a
  future refinement for very large documents.
