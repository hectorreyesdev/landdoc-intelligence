# 0008 — Delete documents (multi-select)

**Status:** Accepted

## What to build
A way to remove an ingested document completely — both its **original file + metadata** (document store,
ADR-0018) and **all of its chunks** (vector store, ADR-0017) — from the backend, surfaced as **multi-select
delete** in the documents table.

1. **`DELETE /documents/{id}`** — removes the document from **both** stores. **Idempotent**: deleting an
   unknown id is a no-op. Returns **`204 No Content`**.
2. **Port extensions** (additive — recorded here per the public-interface guardrail):
   - `IVectorStore.DeleteByDocumentAsync(Guid documentId, CancellationToken)` — drop every chunk with that
     `DocumentId`.
   - `IDocumentStore.DeleteAsync(Guid id, CancellationToken)` — drop the bytes + metadata.
   Implemented in **both** adapters of each port (in-memory + Azure).
3. **Frontend** — the documents table gains a checkbox per row, a select-all checkbox, and a
   **"Delete selected (N)"** action that confirms, calls `DELETE /documents/{id}` for each selected id,
   then reloads the list and closes the viewer if it was showing a deleted document.

## Constraints
- **Backend / TS conventions** per `CLAUDE.md`; `async`/`await` end-to-end. No new NuGet packages or Azure
  resources — deletion uses the existing `Azure.Search.Documents` / `Azure.Storage.Blobs` clients.
- **Vector store deletion is by `documentId`.** Azure AI Search deletes by index **key** (chunk id), so the
  adapter first queries the chunk ids for that `documentId` (the field is `IsFilterable`), then issues a
  delete batch. The in-memory adapter removes matching chunks from its list. (Slice scale: a single
  `Size`-bounded query suffices; paging is a noted future refinement.)
- **Document store deletion** removes both per-document blobs (`"{id}"` and `"{id}.json"`); in-memory removes
  the dictionary entry. Deleting absent blobs/entries is a no-op (idempotent).
- **Endpoint orchestration** lives in the `DELETE` handler: delete chunks, then the document; either store
  failing surfaces as `ProblemDetails` (a retry is safe — delete is idempotent). The `204` is returned
  whether or not the id existed.
- **Frontend:** the single typed client (`api/client.ts`) gains `deleteDocument(id)`; it stays the only
  `fetch` caller (`fetch-discipline` green). Multi-select state + a confirm (`window.confirm`) live in the
  table; selection is cleared and the list reloaded after a delete batch. `ApiResult<T>` pattern preserved.
- **Out of scope:** a single-request bulk endpoint (the client loops per id); soft-delete / undo / trash;
  cascade auditing; auth/RBAC. (A bulk `POST /documents/delete` is a possible later optimization.)

## How to verify
- **Vector store (unit, in-memory):** after adding chunks for two documents, `DeleteByDocumentAsync(idA)`
  leaves only document B's chunks; deleting an unknown id is a no-op.
- **Document store (unit, in-memory):** `SaveAsync` then `DeleteAsync` → `GetAsync`/`GetFileAsync` return
  null and `ListAsync` omits it; deleting an unknown id is a no-op.
- **Endpoint (integration, in-memory adapters):** ingest a fixture, `DELETE /documents/{id}` → `204`; then
  `GET /documents` no longer lists it, `GET /documents/{id}/file` → `404`, and the document's chunks are
  gone from the vector store (a follow-up `/ask` over an otherwise-empty corpus → `409`). Deleting an unknown
  id → `204`.
- **Frontend (component, mocked client):** selecting rows and clicking "Delete selected" (confirm stubbed)
  calls `deleteDocument` once per selected id and triggers a reload; select-all toggles every row; with
  nothing selected the action is disabled.
- **Invariants & build:** `npm run typecheck`, `npm test` (incl. `fetch-discipline`), `npm run build` green;
  backend `dotnet build`/`test` green in CI (new tests included).

## Links
- **Extends:** [[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]] (`IVectorStore`)
  and [[knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata]]
  (`IDocumentStore`); builds on [[knowledge/docs/specs/0006-document-read-back-list-view-original-file]]
  (the documents table this delete UI lives in).
- **Docs to reconcile on merge:** `API.md` (add `DELETE /documents/{id}`) · `ARCHITECTURE.md` /
  `DATA-MODEL.md` (both ports gain a delete operation) · `DATA-FLOW.md` (a delete path) · README feature line.
- **ADRs:** [[knowledge/docs/decisions/0019-hard-best-effort-non-transactional-document-deletion]] records
  the consistency posture (hard delete; best-effort, non-transactional across the two stores; idempotent
  retry as the convergence mechanism). The port *extensions* themselves are additive within the existing
  ADR-0017/0018 designs; no infra change.
