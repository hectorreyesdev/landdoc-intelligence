# 0007 — Insights dashboard + document search & export

**Status:** Accepted

## What to build
A read-only **analytics layer** over the ingested corpus, added to the React SPA — no new backend. The app
gains a simple in-page tab switch between the existing **Workspace** (upload · documents · ask) and a new
**Dashboard**, plus **search/filter + CSV export** on the documents table.

1. **Tab navigation** — a lightweight segmented control in the header (`Workspace` | `Dashboard`); no router
   dependency, no URL change. `useDocumentTable` is lifted to `App` so both tabs share one `GET /documents`
   load (and the post-upload `reload()`).
2. **Dashboard** — all panels aggregate the `DocumentSummary[]` already returned by `GET /documents`
   (id, fileName, status, contentType, chunkCount, `fields[]`, ingestedAt):
   - **KPI tiles:** total documents · total chunks · distinct counties/states · documents **needing review**
     (no fields, or missing a core field) · most recent ingest.
   - **Charts (Recharts):** documents **by state** (and/or county) bar chart; **ingest activity over time**
     (area/line, bucketed by day from `ingestedAt`).
   - **Needs-review list:** documents with empty/partial extraction, each opening the existing viewer.
   - **Lease-expiration widget:** documents bucketed by **expired / ≤30 / ≤60 / ≤90 days / later**, with an
     "expiring soon" table. Source dates are read heuristically from each document's `fields` (a field whose
     name matches `expir|expiration|term end|end date` and whose value parses as a date). When the corpus has
     no such field, the widget shows an honest empty state.
3. **Search/filter + CSV export** (Workspace, documents table):
   - A text filter matching `fileName` and any field name/value; optional state/county quick-filter.
   - **Export to CSV** of the currently-shown documents' fields (a client-side `Blob` download) — uses data
     already in memory; **no new fetch**.

## Constraints
- **Frontend / TS:** React 19 + Vite, `strict: true`, function components + hooks, explicit return types on
  exports, no `any`. New runtime dependency: **`recharts`** (React-19-compatible) for charts only.
- **No backend changes, no new endpoints.** Everything derives from the existing `GET /documents` contract
  (spec 0006). `IVectorStore`, `IDocumentStore`, and the API surface are untouched.
- **Single typed client invariant (ADR-0006):** `api/client.ts` stays the only module that calls `fetch`
  (`fetch-discipline.test.ts` must stay green). CSV export builds a `Blob` from in-memory data and triggers a
  download — no network. The dashboard reads through `listDocuments()` (via `useDocumentTable`), no ad-hoc
  fetch.
- **Aggregation logic lives in pure, tested functions** (e.g. `ui/dashboard/metrics.ts`), separate from the
  presentational chart components — charts render no testable SVG under jsdom, so correctness is proven on the
  pure functions + the KPI/list/table DOM, not chart internals.
- **Field-name matching is heuristic and case-insensitive.** County/State/expiration aggregation depends on
  the extractor emitting reasonably-named discrete fields (ADR-0015's universal core includes County, State,
  Dates); documents lacking a field bucket as "Unknown"/"needs review" rather than erroring.
- **Out of scope:** a backend aggregation endpoint; usage/cost/token telemetry; auth/RBAC and audit; persisted
  saved views or saved questions; client-side routing; **changing the extraction prompt to reliably emit an
  `ExpirationDate`/term field** (a backend follow-up — the widget consumes whatever date-like fields exist and
  degrades gracefully without it).

## How to verify
- **Aggregation (unit, pure functions):** counts (documents, chunks), distinct county/state sets, needs-review
  predicate, ingest-over-time bucketing, and expiration bucketing each return the right shape for crafted
  `DocumentSummary[]` inputs — including empty corpus and documents with no/partial fields.
- **CSV (unit):** the builder emits a header row + one row per document with field values, quoting/escaping
  commas, quotes, and newlines correctly; empty corpus yields just the header.
- **Dashboard (component, mocked client):** KPI tiles show the computed numbers; the needs-review list renders
  the right documents and a row click calls the viewer open handler; the expiration widget shows the empty
  state when no date fields exist and buckets correctly when they do.
- **Search/export (component):** typing in the filter narrows the table; the export control is present and,
  when clicked, produces CSV content matching the builder (assert via a stubbed `URL.createObjectURL` /
  anchor, no real download).
- **Invariants & build:** `npm run typecheck`, `npm test` (incl. `fetch-discipline`), and `npm run build` all
  green; the `frontend-ci` `npm ci` gate stays green (committed `package-lock.json`).

## Links
- **Builds on:** [[knowledge/docs/specs/0006-document-read-back-list-view-original-file]] (the `GET /documents`
  data this consumes) and [[knowledge/docs/specs/0003-frontend-vertical-slice]] (the SPA + single-typed-client
  pattern). Field semantics from [[knowledge/docs/decisions/0015-field-extraction-generic-role-neutral-schema-land-document-types]].
- **Docs to reconcile on merge:** `ARCHITECTURE.md` / `DATA-FLOW.md` (frontend now has Workspace + Dashboard
  tabs over the same `GET /documents` read) · `STACK.md` (add `recharts`) · README feature line.
- **ADRs:** none — no port or infra change; rides entirely on the existing `GET /documents` contract. A future
  backend aggregation endpoint or an extractor change to emit `ExpirationDate` would each get their own
  spec/ADR.

## Amendment — 2026-06-09 (documents in their own tab)
The persisted documents table moved out of the Workspace column into its **own full-width "Documents" tab**
(tabs are now Workspace · Documents · Dashboard). Its Fields column shows a **count** ("N fields") rather
than every field inline — the full set lives in the source-file viewer. Search still matches over all field
names/values and CSV export still includes them. Visual/layout refinement; no contract change.

## Amendment — 2026-06-09 (sortable table · hourly ingest · county map)
Three refinements, all still riding the in-memory `GET /documents` data (no backend/contract change):
- **Sortable documents table.** Every data column (File · Status · Chunks · Fields · Ingested) is a clickable
  header that cycles unsorted → ascending → descending, with `aria-sort` on the active `<th>`. Sorting is a
  pure client-side reorder of the already-filtered rows (`ui/documentSort.ts`, unit-tested); chunks/fields
  sort numerically, ingested chronologically, others by locale string. Default stays server order.
- **Ingest activity by hour.** `ingestByDay` → **`ingestByHour`** (UTC `YYYY-MM-DDTHH:00` buckets) so a corpus
  ingested in one session spreads across hours instead of collapsing to a single point. Axis/tooltip show
  `MM-DD HH:00`.
- **County bubble map.** A new dashboard card renders documents-by-county as proportional bubbles at each
  county's centroid over a US states basemap, **beside the kept "Documents by county" bar chart**. New
  framework-agnostic runtime deps (no React-19 peer conflict — *why not `react-simple-maps`*, which caps at
  React 18): **`d3-geo`**, **`topojson-client`**, **`us-atlas`** (+ `@types/*`). The ~600 KB atlas is
  **dynamically imported** so it stays out of the main bundle. Pure geo math (`documentsByStateCounty` in
  `metrics.ts`; `resolveMarkers` in `ui/dashboard/geo.ts`) is unit-tested with a stub centroid index; the
  projection/SVG is presentational glue, untested under jsdom (same rationale as the Recharts SVG above).
  Documents lacking both a State and a County field don't plot — the map shows an honest empty state.
