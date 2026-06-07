# 0003 — Frontend Vertical Slice (Thin)

**Status:** Accepted · _Amended 2026-06-07 — reconciled the "currently 501" framing: `POST /ask` is
now **live** (spec 0002 merged, PR #10). The `501` path is retained as **defensive** degradation, no
longer the present state; the manual E2E now expects a real cited answer._

## What to build
The React + TypeScript SPA that closes the demo loop — the human-facing front of the RAG slice. It
scaffolds `/frontend` (the first frontend code in the repo) and ships the four-state flow `CLAUDE.md`
names: an **upload control → extracted-fields view → question box → answer-with-citations**. It
consumes the two existing backend contracts — `POST /documents` (ingest write path,
[[knowledge/docs/specs/0001-document-ingestion-write-path]], **live**) and `POST /ask` (read path,
[[knowledge/docs/specs/0002-rag-qa-with-citations]], **now live** — was `501` until spec 0002 merged,
PR #10) — through a **single typed API client**, and it **degrades gracefully** when a call can't be
served.

This spec is deliberately thin on decomposition (React is well-trodden, built directly) and
deliberately precise on the **acceptance target**: the value is a crisp, machine-checkable definition
of "done" so the UI can't be faked or hand-waved ([[agent-proxy-gaming]]). What it pins, and only
this: the UI states and transitions, the two endpoints' request/response shapes, the **501/409/400
degradation behavior**, the rule that **the typed client is the only module that touches `fetch`**,
and the acceptance checks.

Demo-facing capability: an analyst opens the SPA, uploads a land/title PDF, sees the extracted
fields, types a question, and gets a grounded answer with citations — and when a call can't be served
(`501` a not-yet-built endpoint, `409` nothing ingested, `400` bad input), the UI shows a clear,
**non-crashing** state instead of breaking.

## Constraints
- **Stack / scaffold:** React + TypeScript SPA under `/frontend`, `strict: true` (ADR-0006). Dev/build
  = **Vite**, test = **Vitest + React Testing Library**. This **realizes ADR-0006's scaffolding /
  tooling follow-on** and pins the `TODO` frontend rows in `STACK.md` (versions recorded there on
  scaffold). **Function components + hooks only** (no class components); explicit return types on
  exported functions; **no `any`** (use `unknown` + narrow); a single typed client wraps `fetch`.
- **The four UI states (the `CLAUDE.md` flow):**
  1. **Upload** — file input + submit; accepts one PDF; guarded/disabled while a request is in flight.
  2. **Extracted-fields view** — after a `201`, renders `fileName`, `status`, `chunkCount`, and the
     `fields[]` (each `name` / `value`; `sourceChunkId` shown when present).
  3. **Question box** — text input + ask button, enabled once ≥1 document is ingested *(a pre-ingest
     ask is allowed but surfaces the `409` state below)*.
  4. **Answer-with-citations** — renders `answer` and `citations[]`, each citation showing its
     `documentId`, `chunkId`, `score`, and `text` snippet.
  Plus the cross-cutting per-action states: **loading** (in-flight), **empty/initial**, and **error**
  (see degradation). Keep them **visibly distinct**.
- **Endpoint 1 — `POST /documents`** (multipart `file`; spec 0001; **live**). Success `201` →
  `{ id, fileName, status, fields: [{ name, value, sourceChunkId|null }], chunkCount }`. Modeled as a
  TS type mirroring the 0001 contract.
- **Endpoint 2 — `POST /ask`** (`application/json` `{ question }`; spec 0002; **live**, merged PR #10).
  Success `200` → `{ answer, citations: [{ chunkId, documentId, score, text }] }`. Modeled as a TS type
  mirroring the 0002 contract. **Cite-or-nothing holds in the UI too:** never render an `answer`
  without rendering its **≥1** citations.
- **Typed client is the only `fetch` (the structural rule):** a single module (e.g.
  `frontend/src/api/client.ts`) is the **sole** place that calls `fetch`. Components/hooks call typed
  methods (`uploadDocument(file)`, `ask(question)`) returning typed results / typed errors — **no
  ad-hoc `fetch` anywhere else** (CLAUDE.md TS conventions; ADR-0006). Enforced by a repo grep in
  acceptance, not just convention.
- **Degradation behavior — the client maps backend HTTP status → a typed outcome the UI renders;
  never an unhandled throw or blank screen:**
  - **`400`** (blank/whitespace question; missing/empty/non-PDF file) → **inline validation** on the
    offending control ("enter a question" / "choose a PDF"); the form stays usable. The client may
    guard obvious cases pre-flight but must **still** handle a server `400`.
  - **`409`** (ask against an **empty store** — nothing ingested) → a distinct **"ingest a document
    first"** state on the answer area; not a generic error banner.
  - **`501`** (an endpoint not yet implemented) → a distinct **"Q&A isn't available yet"** state;
    **upload + fields keep working independently**. **Defensive:** `/ask` is now live (spec 0002), but
    the UI must still degrade — not crash — for any `501`, and the ingest half must never depend on the
    ask half.
  - **Other non-OK (`5xx` / network failure)** → a generic, retryable **error** state.
  - The backend returns RFC 7807 **ProblemDetails** for these; the client keys off **HTTP status** and
    **tolerates a missing/garbage body** (no hard dependency on parsing the problem payload).
- **Dev wiring:** the **Vite dev proxy** forwards `/documents` and `/ask` to the API; the client uses
  **same-origin relative paths**. **No backend change** — CORS / an absolute base URL are out of scope.
- **Out of scope for this spec:** any change to the `/ask` or `/documents` backend (spec 0002 is
  already merged — this slice only *consumes* the endpoints, and tolerates a `501` defensively) ·
  auth/RBAC · client routing / multi-page · global-state libraries (Redux
  etc.) · a component / design-system library *(assumption: hand-rolled minimal CSS)* · upload
  progress / drag-drop polish · multi-file batch upload · accessibility beyond basic labels ·
  production build / hosting / deploy · streaming answers · TS-type codegen from the API contract
  (ADR-0006 names it a possible later step).

## How to verify
The acceptance target — each item is observably true or it isn't.
- **Scaffold runs:** `cd frontend && npm install`, then `npm run dev` serves the SPA and `npm test`
  passes. These become the real `/frontend` commands `CLAUDE.md` / `RUNBOOK.md` currently mark `TODO`.
- **Upload → fields (component test, mocked client):** given a mocked `uploadDocument` returning a
  0001-shaped `201`, submitting renders the extracted-fields view showing `fileName`, `chunkCount`,
  and **every** `fields[]` entry's `name`/`value` — asserted from the mocked response, not hardcoded
  copy.
- **Ask → answer + citations (component test, mocked client):** given a mocked `ask` returning a
  0002-shaped `200`, asking renders the `answer` **and** every citation's
  `documentId`/`chunkId`/`score`/`text`. **No answer renders without ≥1 citation** — assert an
  answer-with-empty-citations is treated as an error and never shown.
- **Degradation states (one component test per status, state driven purely by the mocked status):**
  - `400` → inline validation on the control; form still usable; no answer rendered.
  - `409` → the "ingest a document first" state on the answer area.
  - `501` → the "Q&A not available yet" state, **and** the upload/fields path still functions in the
    same render (defensive path — `/ask` is live now, but the UI must survive a `501` from any endpoint).
  - `5xx` / network → the generic retryable error state.
- **Typed-client unit tests:** the client maps each status (`201`/`200`/`400`/`409`/`501`/`5xx`) to
  its documented typed outcome — success returns the typed DTO, failures return typed errors (no
  `throw` reaches a component as an unhandled rejection).
- **Fetch discipline (structural, machine-checkable):** `grep -rn "fetch(" frontend/src` (test/setup
  files aside) lists **exactly one module** — the typed client. This is the anti-gaming guard
  ADR-0006's "single typed client" implies.
- **TS strictness:** `tsc --noEmit` (or the build) passes under `strict: true`; **no `any`** in
  `frontend/src`; exported functions carry explicit return types.
- **Manual end-to-end (live API):** with the backend running and the Vite proxy on — upload
  `synthetic-lease-01.pdf` → fields render; ask a question → **`/ask` is live (spec 0002)**, so the UI
  renders the real `answer` with its citations resolving to source chunks. *(The `501` "not available
  yet" state is exercised by the component test above — it's the defensive path, no longer the live
  one.)*
- **Suite green (tdd):** `npm test` passes; every behavior above is covered by tests written
  test-first.

## Links
- **Consumes:** [[knowledge/docs/specs/0001-document-ingestion-write-path]] (`POST /documents`, live) ·
  [[knowledge/docs/specs/0002-rag-qa-with-citations]] (`POST /ask`; **now live**, merged PR #10 — the UI
  still degrades defensively on a `501`).
- **ADRs:** [[knowledge/docs/decisions/0006-react-typescript-frontend-over-blazor]] (React + TS SPA,
  one typed `fetch` client — this spec realizes its scaffolding / tooling follow-on) ·
  [[knowledge/docs/decisions/0004-modular-monolith-over-microservices]] (the HTTP/JSON SPA↔API
  boundary) · [[knowledge/docs/decisions/0009-corpus-wide-ask-retrieval-scope]] (global `/ask`;
  citations carry `documentId`).
- **Note (forward):** [[agent-proxy-gaming]] — why the *spec*, not the demo, is the acceptance target.
- **Docs to reconcile on merge:** `STACK.md` (pin the frontend rows: React / TypeScript / Vite /
  Vitest versions) · `RUNBOOK.md` (real `/frontend` install/run/test + the Vite proxy) ·
  `ARCHITECTURE.md` (SPA component + typed-client boundary) · `CLAUDE.md` (drop the "`/frontend` not
  scaffolded" `TODO`) · `API.md` (note the SPA consumes `/documents` + `/ask`).
- **Tooling pin:** Vite + Vitest are recorded here under ADR-0006's umbrella; if you'd rather formalize
  the frontend-tooling choice on its own, `/adr` can capture it separately — not required to accept
  this spec.
- **Implementing PR:** _TBD — link once opened._
