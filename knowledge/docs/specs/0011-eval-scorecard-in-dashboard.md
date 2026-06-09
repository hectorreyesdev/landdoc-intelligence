# 0011 — Eval answer-quality scorecard on the Dashboard

**Status:** Accepted

## What to build
Surface the latest answer-quality eval run ([[knowledge/docs/specs/0012-rag-answer-quality-eval-harness]])
in the SPA, as a read-only **scorecard** on the Dashboard tab. The eval is on-demand, offline, paid, and
non-deterministic — it is **not** on the app's request path — so this shows a **committed snapshot** of the
most recent run, clearly dated, not live data.

1. The eval runner emits a compact **`eval-summary.json`** (metric means + per-case rows + run metadata)
   after a run, so a snapshot can be promoted into the SPA and committed.
2. The Dashboard renders an **"Answer quality (eval)"** card from that snapshot (bundled at build time):
   three KPI tiles (recall@k, groundedness, correctness), a per-case table, an "as of `<date>`" caption,
   and a link to the eval methodology / full report.

## Constraints
- **Summary artifact (eval project):** `EvalPipelineFixture` collects each case's metric values (the test
  records them) and writes `eval-summary.json` to the result-store root on dispose. Schema (stable —
  matches the committed file the SPA imports):
  ```json
  {
    "generatedAt": "<ISO-8601 UTC>", "judgeModel": "claude-sonnet-4-6", "caseCount": 18,
    "means": { "recallAtK": 1.0, "groundedness": 5.0, "equivalence": 4.78 },
    "cases": [ { "id": "midland-royalty", "recallAtK": 1.0, "groundedness": 5.0, "equivalence": 5.0, "abstained": false } ]
  }
  ```
  `abstained` = the answer equals the exact abstain string. Means are over non-null metric values. No
  secrets; no app/API change; the eval stays excluded from `LandDoc.slnx`.
- **Frontend (`/frontend`, spec 0006/0007 conventions):** TypeScript `strict`, function components +
  hooks, explicit return types, **no `any`**. The snapshot is a **build-time `import` of a committed
  `eval-summary.json`** — NOT a `fetch` — so the single-typed-client / single-fetch invariant
  (`fetch-discipline.test.ts`, ADR-0006) is untouched and no API endpoint is added. A typed `EvalSummary`
  shape guards the import.
- **Placement:** a new `EvalQualityCard` component on the **Dashboard**, rendered **independently of the
  document list** (it reflects model quality over the eval corpus, not the user's uploaded docs) — so it
  shows even when no documents are ingested / while the corpus data is loading. Reuses existing
  `kpi-tile` / `panel` styling.
- **Refresh flow (documented, manual):** re-run the eval → copy the emitted `eval-summary.json` over the
  committed copy → redeploy. The card's date makes staleness obvious.
- **Out of scope:** live/auto-refreshing results; a `GET /eval/summary` endpoint + blob upload (a heavier
  alternative, deferred); hosting the full interactive `aieval` HTML in-app (link out instead);
  thresholds/gating (spec 0010).

## How to verify
- **Eval emitter:** after a run, `eval-summary.json` exists in the result-store root with the schema above;
  means equal the per-case averages; `caseCount` matches the dataset.
- **Frontend (Vitest + RTL):** `EvalQualityCard` renders the three metric tiles with the snapshot values,
  a row per case (with an "abstained" marker where applicable), and the "as of" date; the Dashboard shows
  the card even with an empty document list. `fetch-discipline.test.ts` stays green (no new `fetch`).
- **Suite green:** `npm test`, `npm run build` (typecheck) pass; `dotnet build backend/eval/LandDoc.Evals`
  compiles; `dotnet test LandDoc.slnx` still green and the eval runner still absent from the solution.

## Links
- **Shows:** [[knowledge/docs/specs/0012-rag-answer-quality-eval-harness]] results · post-tuning baseline
  from [[knowledge/docs/specs/0010-rag-answer-quality-tuning]].
- **Builds on:** [[knowledge/docs/specs/0007-insights-dashboard-and-document-search-export]] (the Dashboard)
  · [[knowledge/docs/decisions/0006-react-typescript-frontend-over-blazor]] (single typed client / fetch
  discipline).
- **Operations:** [[knowledge/docs/EVAL-HARNESS.md]] (run + the snapshot refresh flow).
