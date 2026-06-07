# Frontend architecture — how the SPA is wired

The *what-we-know* view of the `/frontend` React+TS SPA (spec
[[knowledge/docs/specs/0003-frontend-vertical-slice]]). The slice that proves upload → fields →
ask → cited answer in the browser.

## Stack
Vite + React + TypeScript (`strict`); tests with Vitest + React Testing Library (jsdom). Versions
are pinned in `frontend/package.json` (React 19 · TS 6 · Vite 8 · Vitest 4) and mirrored into
`STACK.md`. Function components + hooks only; explicit return types on exports; no `any`.

## The one rule that shapes everything: a single typed client
`src/api/client.ts` is the **only** module that calls `fetch` (CLAUDE.md convention; ADR-0006).
Components/hooks call `uploadDocument(file)` / `ask(question)` and get back a discriminated
`ApiResult<T>` — never a raw response, never a thrown rejection. `src/api/types.ts` mirrors the
backend contracts (specs 0001/0002). A Vitest test (`src/fetch-discipline.test.ts`) + a repo grep
assert the single-`fetch` invariant so it can't rot as the UI grows.

## Transport: single-origin, no CORS ([[knowledge/docs/decisions/0011-single-origin-spa-api-topology]])
The client uses **relative paths** (`/documents`, `/ask`) — no base URL, no env var. Dev: the Vite
dev-proxy (`vite.config.ts`) forwards those to `http://localhost:5084` — the *only* place that
absolute URL appears. Prod (named, not built): an Azure Static Web Apps linked backend gives the
same single-origin shape. So CORS never enters the architecture.

## Status → typed outcome → UI state
The client maps HTTP status to a typed error kind (it keys off status and tolerates a missing/garbage
ProblemDetails body): `400`→validation · `409`→empty-store · `501`→not-implemented · other→server ·
fetch-threw→network. The UI renders a **distinct, non-crashing** state per kind:
- **400** → inline validation (form stays usable)
- **409** → "ingest a document first"
- **501** → "Q&A is not available yet" — *defensive*; `/ask` is live today, but the UI must survive it
- **5xx / network** → generic retryable error

Two invariants: **cite-or-nothing** (never render an answer without ≥1 citation) and the
**upload/fields path never depends on the ask path** (asserted by an App-level 501 test).

## Testing posture (pragmatic, per the [[workflow-harness]] tdd skill)
Component tests drive each state from a **mocked client** (`vi.mock('../api/client')`); typed-client
unit tests cover the status map offline. Gotchas worth remembering live in `lessons.md` (RTL
`getByLabelText` colliding with a region's `aria-labelledby`; needing `afterEach(cleanup)`).

## CI
`.github/workflows/frontend-ci.yml` — separate from the backend `ci.yml` (paths filters are
workflow-level), paths-filtered to `frontend/**`, runs `npm ci` → `npm run typecheck` → `npm test`
on Node 22. `npm ci` enforces the committed lockfile. Offline — no secrets.

## What needs the API key (local only)
The real cited-answer E2E needs the backend running with `ModelClient:ApiKey` (via
`dotnet user-secrets`) — the slice backend builds the chat client per request, so without it every
call 500s. Never wired into CI; the frontend tests are fully offline.
