# 0005 — Ingest Markdown and Text Documents

**Status:** Accepted

## What to build
Extend the ingest write path ([[knowledge/docs/specs/0001-document-ingestion-write-path]]) so
`POST /documents` accepts **Markdown** (`.md` / `.markdown`) and **plain-text** (`.txt`) uploads in
addition to text-based PDFs. The demo-facing capability and the response are unchanged — an upload
still returns the new document id, its extracted fields, `status`, and `chunkCount` — but an analyst
can now drop in a lease saved as a `.txt` or `.md` file, not only a PDF.

For a text or markdown upload the uploaded bytes **are** the document text, so there is no parsing
step: the endpoint decodes the bytes as UTF-8 and the resulting text flows straight into the existing
`chunk → embed → store` and best-effort field-extraction path, unchanged. PDF uploads keep parsing
through PdfPig exactly as today.

This **changes spec 0001's input contract.** Today the endpoint is PDF-only and rejects any non-PDF
with `400` (`LooksLikePdf` in [DocumentsEndpoints.cs](../../../backend/src/LandDoc.Api/Ingestion/DocumentsEndpoints.cs)).
The accepted set becomes **{ PDF, `.txt`, `.md`, `.markdown` }**; anything else still `400`s. Spec
0001's blanket *"a non-PDF returns 400"* acceptance check is **superseded** by the narrower
*"an unsupported file type returns 400"* defined here.

## Constraints
- **Backend / module:** ASP.NET Core Web API on **.NET 10 (LTS)** under `/backend` (ADR-0003),
  `Ingestion` module (ADR-0004). C# conventions per `CLAUDE.md`: nullable enabled, `async`/`await`
  end-to-end, constructor DI, file-scoped namespaces, `record` DTOs, validate/throw early.
- **Endpoint shape unchanged:** `POST /documents`, `multipart/form-data` with a single `file` part
  (one file per request). Response `201` with the **same** body
  `{ id, fileName, status, fields, chunkCount }` — **no response-contract change**, `fileName` still
  echoes the upload.
- **Format detection by filename extension (decided 2026-06-08):** the endpoint dispatches on
  `Path.GetExtension(fileName)`, case-insensitive:
  - `.pdf` → must still pass the existing `%PDF-` magic-byte guard (a `.pdf` whose bytes are not a
    PDF → `400`, as today).
  - `.txt`, `.md`, `.markdown` → treated as UTF-8 text; the raw bytes are decoded to the document
    text. *(assumption: a UTF-8 BOM is tolerated; markdown is chunked as **raw text** — no markdown
    stripping or rendering for the slice.)*
  - any other extension, or no extension → `400` `ProblemDetails`; nothing is stored.
- **Text-extraction seam:** the pipeline must select **PDF-parse vs UTF-8-decode by format** instead
  of always calling the PDF parser ([DocumentIngestionService.cs](../../../backend/src/LandDoc.Api/Ingestion/DocumentIngestionService.cs)
  unconditionally calls `pdfTextExtractor.Extract` today). This is an **internal** change to the
  `Ingestion` module. It must **not** change the `IChatClient` / `IEmbeddingClient` ports or the
  stored `Chunk` contract `{ Id, DocumentId, Text, Vector }` (the 0001→0002 citation seam stays
  intact). *(assumption: carry a small format/content-type value from the endpoint into the service,
  or add a text-extractor dispatcher — exact shape is the implementer's call, as long as the ports
  and `Chunk` shape are untouched. If the seam genuinely needs a port-shaped change, stop and record
  an ADR — the intent here is **no port change**.)*
- **Extraction stays best-effort and unchanged:** field extraction still runs on the decoded text via
  the `Extraction` module's `IChatClient`. Arbitrary `.md`/`.txt` that isn't a land/title document may
  yield an **empty `fields`** array — that is the existing best-effort behavior (0001 amendment): it
  returns `201`, never `500`. No new extraction logic is added here.
- **Chunk count reflects actual content:** short text files may legitimately produce a small number of
  chunks. Unlike 0001's lease fixture (which asserts N > 1), an arbitrary text upload only needs
  `chunkCount` ≥ 1; no new "document too short / no text" `400` is introduced for the slice.
- **Out of scope for this spec:** multi-file batch upload (still one file per request); other formats
  — `.docx`, `.rtf`, `.html`, scanned-image PDFs / **OCR** (PRD non-goal); markdown rendering or
  formatting strip; content-type-header or content-sniffing detection (extension-based was chosen);
  any change to the `201` response shape; the read/retrieval path; auth/RBAC; Azure AI Search; Azure
  AI Document Intelligence.

## How to verify
- **Markdown happy path (integration, `WebApplicationFactory`, fake `IChatClient`):** `POST /documents`
  with a small synthetic-lease `.md` fixture returns `201` with a non-empty `id`, `fileName` echoing
  the upload, `status` `"ready"`, and `chunkCount` = N (N ≥ 1); the in-memory store then holds exactly
  N chunks for that id, each with a non-empty `float[]` embedding **all of equal length**, and each
  retaining its source `Text` under a stable `Id` carrying the correct `DocumentId` (0001→0002 seam).
- **Plain-text happy path:** the same assertions for a `.txt` upload → `201`, N chunks stored with the
  same invariants.
- **Extraction over text:** with the fake `IChatClient` returning canned fields, those exact fields
  appear in the response for a `.md`/`.txt` upload — proving the decoded text flows through the
  `Extraction` port unchanged.
- **Text is decoded, not PDF-parsed:** a `.txt`/`.md` upload whose bytes are **not** a valid PDF
  ingests successfully (no PdfPig parse attempt, no `400`) — demonstrating the UTF-8-decode branch was
  taken rather than the PDF parser.
- **PDF path unchanged (regression):** the existing 0001 PDF happy path still returns `201` with
  `chunkCount` N > 1 — the new branch does not break PDF parsing.
- **Unsupported type rejected:** `POST /documents` with an unsupported extension (e.g. `.png` or
  `.docx`), or a `.pdf` whose bytes fail the magic-byte guard, returns `400` `ProblemDetails`; nothing
  is added to the store. *(This supersedes 0001's blanket "non-PDF → 400".)*
- **Missing / empty still 400:** no `file` part, or an empty file, returns `400` `ProblemDetails` with
  nothing stored (unchanged from 0001).
- **Suite green (tdd):** `dotnet build` and `dotnet test` pass; the behaviors above are covered by new
  tests written test-first, with a `.md` and a `.txt` fixture added under the backend test assets.

## Links
- **Changes / supersedes:** [[knowledge/docs/specs/0001-document-ingestion-write-path]] — extends its
  input contract (PDF-only → PDF + `.txt`/`.md`/`.markdown`); 0001's *"non-PDF returns 400"* acceptance
  check is superseded by the *"unsupported type returns 400"* check above. Reconcile 0001's
  `Endpoint` / `Errors` / `How to verify` wording on merge.
- **Docs to reconcile on merge:** `API.md` (accepted content types for `POST /documents`) ·
  `DATA-FLOW.md` (ingest sequence: format dispatch → PDF parse **vs** UTF-8 decode) ·
  `GLOSSARY.md` if a "supported document format" term helps.
- **ADRs:** none new — this rides on the existing `Ingestion` module and the two model-access ports
  ([[knowledge/docs/decisions/0004-modular-monolith-over-microservices]],
  [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]). The seam is
  deliberately port-neutral; a port-shaped change would require its own ADR.
- **Implementing issue / PR:** _TBD — link once opened._
