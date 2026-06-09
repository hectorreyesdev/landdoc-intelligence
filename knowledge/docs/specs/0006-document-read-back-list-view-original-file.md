# 0006 — Document read-back: list, detail, original-file view, and citation source labels

**Status:** Accepted

## What to build
Give the slice a **persisted document library** on top of the chunk store, realizing
[[knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata]]:

1. Persist each ingested document — its **original uploaded bytes**, plus metadata (file name, status,
   content type, chunk count, the extracted **fields**, ingest timestamp) — via a new `IDocumentStore` port.
2. New read endpoints:
   - **`GET /documents`** → list every persisted document's metadata + fields. Empty store → **`200 []`**.
   - **`GET /documents/{id}`** → one document's metadata + fields; **`404`** `ProblemDetails` if unknown.
     (Promotes the "intended — not yet specced" entry in `API.md`.)
   - **`GET /documents/{id}/file`** → the **original file bytes**, served **inline** with the correct
     `Content-Type` so a browser `<iframe>`/`<object>` renders it; **`404`** if unknown.
3. Enrich the `/ask` **`Citation`** with the source file name so the UI labels and links each citation to its
   document (the [[knowledge/docs/decisions/0014-surface-source-document-identity-in-ask-grounding-context]]
   follow-on).
4. Frontend slice: a **documents table** (loaded from `GET /documents` on mount, persists across reload) shown
   **alongside** the existing session card grid; a **document viewer** (modal/drawer) that shows the fields and
   embeds the original file; and **clickable citations** in the answer that open the viewer for that document.

## Constraints
- **Backend / module:** ASP.NET Core Web API on **.NET 10 (LTS)** under `/backend` (ADR-0003); new storage
  adapter lives in the `Storage` namespace alongside `IVectorStore` (ADR-0004). C# conventions per `CLAUDE.md`:
  nullable enabled, `async`/`await` end-to-end (no `.Result`/`.Wait()`), constructor DI, file-scoped
  namespaces, one public type per file, `record` DTOs, validate/throw early.
- **New port (public contract — recorded in ADR-0018):**
  `IDocumentStore` with `SaveAsync(DocumentMetadata, byte[] originalBytes, ct)`, `ListAsync(ct)`,
  `GetAsync(Guid, ct) → DocumentMetadata?`, `GetFileAsync(Guid, ct) → DocumentFile?` (null ⇒ 404).
  `DocumentMetadata(Guid Id, string FileName, string Status, string ContentType, int ChunkCount,
  IReadOnlyList<ExtractedField> Fields, DateTimeOffset IngestedAt)`;
  `DocumentFile(byte[] Content, string ContentType, string FileName)`.
- **Config-selected provider** `DocumentStore:Provider` (mirrors `VectorStore:Provider`): `azureblob` = live
  default, `inmemory` = offline/test. Registered **singleton** (write + read share one instance, like
  `IVectorStore`). Tests pin `DocumentStore:Provider=inmemory` via `TestModuleInitializer` so the suite is
  fully offline-green (no storage credentials in CI).
- **Azure Blob adapter (live):** container `documents` on `stlanddochr01` (already provisioned). Two blobs per
  document — `"{id}"` (bytes, with `ContentType`) and `"{id}.json"` (metadata). Auth:
  `Blob:ServiceUri` + `DefaultAzureCredential` if set, else `Blob:ConnectionString` (ADR-0016 posture).
  Idempotent `CreateIfNotExists`. All async SDK overloads.
- **Ingest change (internal to `Ingestion`):** `DocumentIngestionService` also calls
  `IDocumentStore.SaveAsync` with the uploaded bytes + computed metadata. Content type is derived from the
  upload format/extension (`.pdf`→`application/pdf`, `.txt`→`text/plain`, `.md`/`.markdown`→`text/markdown`).
  This persistence is **required** — a blob-write failure fails the request (unlike best-effort extraction,
  which is unchanged). The `POST /documents` `201` response shape is **unchanged**.
- **`IVectorStore` is untouched** — we serve the original file, so no chunk reassembly/ordinal is needed.
- **`Citation` change (public `/ask` response):** add `string Source` (the file name), populated from
  `Chunk.Source`. Additive field; amends spec 0002's `Citation` shape. `/ask` request and retrieval scope
  (corpus-wide, ADR-0009) are unchanged.
- **Frontend:** the single typed API client (`api/client.ts`) is the only `fetch` caller
  (`fetch-discipline.test.ts` enforces it); all methods return `ApiResult<T>` (never throw). The file is fetched
  by the **browser via a relative URL** in an `<iframe>` (same-origin, ADR-0011/0016) — exposed as a pure
  `documentFileUrl(id)` helper in the client, **not** through `fetch`/`ApiResult` (so bytes stay out of the
  result type and the viewer touches no `fetch`). TypeScript `strict`, function components + hooks, explicit
  return types, no `any`. The new table is **additive**: the existing `DocumentList`/`DocumentCard` session
  grid stays; the persisted table renders as a separate section and reflects newly uploaded docs (upload →
  table `reload()`).
- **Out of scope:** delete / re-ingest / overwrite; pagination or search over the document list; markdown
  rendering or syntax highlighting in the viewer (text is served inline as-is); thumbnails; auth/RBAC; OCR /
  Azure AI Document Intelligence; any change to `IVectorStore` or the `POST /documents` / `/ask` request
  contracts.

## How to verify
- **Store contract (unit, `InMemoryDocumentStore`):** `SaveAsync` then `GetAsync` returns the same metadata;
  `GetFileAsync` returns the same bytes + content type; unknown id → `null` for both; `ListAsync` returns all
  saved documents and an **empty list** for an empty store.
- **Ingest persists the document (integration, `WebApplicationFactory`, fake `IChatClient`, in-memory stores):**
  after `POST /documents` with the synthetic-lease fixture, the `IDocumentStore` holds one document whose bytes
  equal the upload and whose `ChunkCount` matches the `201` response, with the fake-extracted `Fields`.
- **List:** ingest one doc, then `GET /documents` → `200` with one entry (id matches, `fileName` echoes upload,
  `fields` non-empty, `chunkCount` matches). Empty store → `200 []` (not `404`).
- **Detail:** `GET /documents/{id}` → `200` metadata; unknown id → `404` `ProblemDetails`.
- **File:** `GET /documents/{id}/file` → `200`, body bytes equal the uploaded fixture, `Content-Type:
  application/pdf`; an ingested `.md`/`.txt` returns `text/markdown` / `text/plain`; unknown id → `404`.
- **Citation source:** `POST /ask` over the ingested corpus returns citations whose `source` is non-empty and
  equals the ingested file name (mirrors the existing `QaPassage` source-name assertion, now on the `Citation`
  DTO).
- **Frontend (Vitest + RTL, mocked client):** `listDocuments`/`getDocument` map status→`ApiResult` like `ask`;
  `documentFileUrl('x')` === `/documents/x/file`; `DocumentsTable` renders rows + field columns and "View"
  calls `onOpenDocument(id)`, empty list shows an empty state; `DocumentViewer` renders the fields and an
  `<iframe src="/documents/{id}/file">`, and closes on backdrop/Escape; a citation renders the file name and
  clicking it calls `onOpenDocument(documentId)`; `fetch-discipline.test.ts` stays green.
- **Suite green (tdd):** `dotnet build` + `dotnet test` and `npm test` pass, behaviors covered by tests written
  test-first; the in-memory adapter keeps everything offline.

## Links
- **Realizes:** [[knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata]]
  (the port, Blob layout, auth, required-persistence, and `Citation.Source` decisions).
- **Amends:** [[knowledge/docs/specs/0002-rag-qa-with-citations]] — `Citation` gains `Source` (additive). Builds
  on [[knowledge/docs/specs/0001-document-ingestion-write-path]] (the write path now also persists the document)
  and [[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]] (chunk persistence; this is
  its named Step 2 + Step 3).
- **Docs to reconcile on merge:** `API.md` (promote `GET /documents/{id}`; add `GET /documents` + `/file`; add
  `Citation.source`) · `DATA-MODEL.md` (persisted Document entity + Blob store + `Citation.Source`) ·
  `ARCHITECTURE.md` (`IDocumentStore` port + adapters + `DocumentStore:Provider`) · `DATA-FLOW.md` (ingest also
  writes the blob; viewer read path) · `DEPLOYMENT.md` / `CICD.md` (role grant + `Blob__ServiceUri` /
  `DocumentStore__Provider` env vars) · `RUNBOOK.md` (local Azurite / in-memory) · `AZURE-CONFIG.md` (mark §6.4
  done; record the role grant).
- **ADRs:** [[knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata]]
  (new) · realizes the follow-on from
  [[knowledge/docs/decisions/0014-surface-source-document-identity-in-ask-grounding-context]].
