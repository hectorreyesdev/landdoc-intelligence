# 0018. Persisted document store (Azure Blob) for original files + document metadata

- Status: Accepted
- Date: 2026-06-08
- Builds on: [ADR-0017](0017-azure-ai-search-free-tier-live-vector-store.md), [ADR-0016](0016-single-container-azure-container-apps-keyvault-secrets.md)
- Realizes the follow-on flagged by: [ADR-0014](0014-surface-source-document-identity-in-ask-grounding-context.md)

## Context
[[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]] brought persistence to the
**chunk** layer and explicitly named "a foundation for the document list (Step 2) and original files
(Step 3)" as the next steps. This ADR is Steps 2 + 3.

Today the only persisted unit is `Chunk{Id, DocumentId, Text, Vector, Source}` in `IVectorStore`. There is
**no document registry**: no stored document record, **no original file bytes**, and **no persisted extracted
fields** — the fields computed at ingest are returned once in the `POST /documents` response and then lost
(`DATA-MODEL.md`). The UI's document list lives only in browser memory for the session. Two user-facing
capabilities can't be served by the current model:

1. A **documents table** listing all ingested documents (surviving restart/redeploy) with their **extracted
   fields**.
2. **Viewing the original source document** — opening the real uploaded PDF/text from a citation or a table
   row.

Both require persisting new, document-grained data. Constraints that shape the choice:
- We deliberately want to show the **original file**, not a reconstruction. So we must store the **uploaded
  bytes** verbatim. (This also means we do *not* need a chunk ordinal or text reassembly — `IVectorStore`
  stays untouched.)
- The chunk store is **Azure AI Search Free tier** (ADR-0017): 50 MB cap, designed for similarity search over
  256-d vectors — **not** a place to park PDF byte payloads.
- The infra already exists (`AZURE-CONFIG.md` §2, §6.4): storage account **`stlanddochr01`** + blob container
  **`documents`**, with `Blob--ConnectionString` already a Key Vault secret, and §6.4 already names "Blob
  document store — replaces local-disk uploads; container `documents`" as planned work. So this wires an
  adapter to provisioned infra; it does not provision net-new storage.
- ADR-0014 already flagged "also return the source name in `Citation` so the UI shows the file name instead of
  the GUID — its own contract change." This ADR carries that follow-on.

## Decision
We will introduce a **persisted document store as a new port `IDocumentStore`**, config-selected exactly like
`IVectorStore` (ADR-0017), with an **Azure Blob Storage** live adapter and an **in-memory** offline/test
adapter; enrich `Citation` with the source file name; and persist the original bytes + document metadata
(including extracted fields) at ingest.

**1. A new port, not an extension of `IVectorStore`.** The vector store is a chunk-grained similarity index
(ANN over 256-d vectors); document storage is document-grained object storage (byte payloads + metadata with
distinct lifecycle). Mixing them would force PDF bytes into the 50 MB Free-tier search index and couple
unrelated concerns. So:
```
IDocumentStore
  SaveAsync(DocumentMetadata metadata, byte[] originalBytes, ct)
  ListAsync(ct)                  -> IReadOnlyList<DocumentMetadata>
  GetAsync(Guid id, ct)          -> DocumentMetadata?   (null => 404)
  GetFileAsync(Guid id, ct)      -> DocumentFile?        (null => 404)
```
`DocumentMetadata{ Id, FileName, Status, ContentType, ChunkCount, IReadOnlyList<ExtractedField> Fields,
DateTimeOffset IngestedAt }`; `DocumentFile{ byte[] Content, string ContentType, string FileName }`. A
`DocumentStore:Provider` switch (mirroring `VectorStore:Provider`) selects `azureblob` (live default) or
`inmemory` (offline/test).

**2. Azure Blob layout — two blobs per document** in the `documents` container: `"{id}"` holds the raw
uploaded bytes (with `BlobHttpHeaders.ContentType`), `"{id}.json"` holds the metadata JSON. `ListAsync`
enumerates the `*.json` blobs and deserializes them (cheap at this corpus size). Rejected alternative: a second
Azure AI Search index for document metadata — it would re-introduce 50 MB-cap pressure, put bytes where they
don't belong, and add a moving part; Blob is the natural object store and is already provisioned.

**3. Auth — managed-identity-preferred, connection-string fallback** (consistent with ADR-0016's "one
credential, two contexts"): if `Blob:ServiceUri` is set, use `BlobServiceClient(new Uri(serviceUri), new
DefaultAzureCredential())` (passwordless — the hosting path); else fall back to `Blob:ConnectionString` (the
already-provisioned KV secret, and Azurite/local dev). The container is created idempotently on startup
(`CreateIfNotExists`, mirroring the index-ensure in `AzureAiSearchVectorStore`). The passwordless path needs a
new role grant: **Storage Blob Data Contributor** for the Container App's managed identity on `stlanddochr01`.

**4. Document persistence is required (not best-effort).** Field extraction stays best-effort (a down chat
provider must not fail ingest — spec 0001 amendment). But persisting the document is the foundation of the
"view source" feature, so a blob-write failure **fails the ingest write path** (surfaces as a provider error),
rather than silently producing a document that can't be viewed. The asymmetry is deliberate.

**5. `Citation` gains `Source`** — `Citation(Guid ChunkId, Guid DocumentId, double Score, string Text, string
Source)`, populated from `Chunk.Source` (already the sanitized file name; no store change). This realizes the
ADR-0014 follow-on and is a public-contract touch to spec 0002, carried in spec 0006.

Tests pin `DocumentStore:Provider=inmemory` assembly-wide via the existing `TestModuleInitializer` (alongside
the `VectorStore__Provider=inmemory` pin) so CI — which has no storage credentials — stays green.

## Consequences
- Ingested documents — **original bytes + metadata + extracted fields** — **persist across restarts and
  redeploys**; reusing the already-provisioned `stlanddochr01` / `documents` at **$0** net-new.
- New read surface (carried by spec 0006): `GET /documents`, `GET /documents/{id}`, `GET /documents/{id}/file`.
- One genuinely new infra action: the **Storage Blob Data Contributor** role grant for the app identity (only
  needed for the passwordless `ServiceUri` path; connection-string fallback needs no grant).
- Ingest now does an extra blob write (two blobs) — small added latency, and a new required failure mode.
- The provider default is `azureblob` (production), so — exactly as ADR-0017 warns — the production default is
  part of the test surface; tests **must** pin `inmemory` or CI breaks. The `TestModuleInitializer` pin is the
  guard.
- `IVectorStore` is **unchanged** (we show the original file, so no chunk reassembly is needed).
- Production-at-scale stays a tier/config change, not code: the same adapter works against any storage account;
  swapping back to fully-in-memory is a `DocumentStore:Provider` flip.

## Notes for implementation (non-binding)
- New NuGet dependency: `Azure.Storage.Blobs`.
- New config keys: `DocumentStore:Provider`, `Blob:ServiceUri` (non-secret, the
  `https://stlanddochr01.blob.core.windows.net` form), `Blob:ContainerName` (default `documents`), and the
  existing `Blob:ConnectionString` (KV `Blob--ConnectionString`) as the fallback.
- Spec 0006 carries the endpoint shapes, status codes, the frontend slice, and the verification plan.
