# 0009 — LLM usage & cost ops dashboard

**Status:** Accepted

## What to build
An **operations view of LLM usage** for the Foundry-hosted Azure OpenAI deployments — token usage,
a per-deployment breakdown, **estimated** cost, request volume with error/429 counts, and response
latency. It is aimed at a different audience than the existing analyst **Dashboard** (spec 0007):
the **operator/owner** who wants to see what the models are costing and how they're behaving, not
the land analyst looking at the corpus. It surfaces as a new **"Ops / Usage"** tab in the SPA
(*assumption: tab label "Ops / Usage", `Tab` id `'usage'`*), alongside Workspace · Documents ·
Dashboard.

It is backed by a single new read endpoint, **`GET /usage`**, which reads **live** from Azure
Monitor **platform metrics** for the Foundry resource each time it's called — there is no stored
usage history in the app. Cost is **computed** from a configured price table (tokens × USD-per-1K),
so it is an honest **estimate**, not the Azure invoice. The whole feature is read-only, $0 of new
infrastructure, and introduces no new secret — it realizes [[knowledge/docs/decisions/0020-llm-usage-cost-observability-azure-monitor-metrics]].

The capability the slice gains: open the Ops / Usage tab, pick a time range (24h / 7d / 30d), and
see total tokens + estimated cost, a per-deployment table (`gpt-5.4-mini`, `text-embedding-3-small`),
a request-health card (success / 4xx / 429 / 5xx), and a latency card (avg / max ms).

## What to build — backend

1. **New port `IUsageSource`** (public contract; recorded in ADR-0020), in `Storage/` alongside the
   other ports, config-selected via `UsageSource:Provider` exactly like `VectorStore:Provider` /
   `DocumentStore:Provider`:
   ```csharp
   public interface IUsageSource
   {
       Task<UsageData> GetUsageAsync(UsageRange range, CancellationToken cancellationToken);
   }
   ```
   - `UsageRange` — enum `Last24h | Last7d | Last30d`.
   - **The port returns RAW aggregates only — no cost.** Cost is computed *outside* the adapter (see
     §3) so both providers return identical token/request/latency data and cost is provider-independent
     and unit-testable. This refines ADR-0020's "…plus computed cost": the *computation* lives in a
     pure calculator, not in the adapter.
   - `UsageData` (records, `Model/` namespace):
     ```
     UsageData(DateTimeOffset From, DateTimeOffset To,
               TokenAggregate Totals,
               IReadOnlyList<DeploymentUsage> Deployments,
               RequestSummary Requests,
               LatencySummary Latency)
     TokenAggregate(long PromptTokens, long CompletionTokens, long TotalTokens)
     DeploymentUsage(string Deployment, long PromptTokens, long CompletionTokens, long TotalTokens)
     RequestSummary(long Total, long Success, long ClientErrors, long Throttled429, long ServerErrors)
     LatencySummary(double AvgMs, double MaxMs)
     ```
     `TotalTokens = PromptTokens + CompletionTokens` (computed for internal consistency; the Azure
     `TokenTransaction` metric is the platform's own total and is used by the live adapter as a
     corroborating source, not the response value).

2. **Live adapter `AzureMonitorUsageSource : IUsageSource`** (`Azure.Monitor.Query` `MetricsQueryClient`,
   `DefaultAzureCredential` — managed identity in hosting, `az login` locally; consistent with ADR-0016).
   - Queries the Foundry resource id from **non-secret** config `Monitor:ResourceId`. **Throws at
     construction if `Monitor:ResourceId` is unset** (validate/throw early, mirroring
     `AzureBlobDocumentStore`'s ctor guard) — so a misconfigured live provider fails fast, not per request.
   - Metrics pulled (Azure Monitor namespace for the Cognitive Services / OpenAI account), **split by the
     `ModelDeploymentName` dimension** to produce the per-deployment rows:
     - `ProcessedPromptTokens` (sum) → `PromptTokens`
     - `GeneratedTokens` (sum) → `CompletionTokens`
     - `TokenTransaction` (sum) → corroborating total (not serialized)
     - `AzureOpenAIRequests` (sum, **split by `StatusCode`**) → bucketed into the `RequestSummary`:
       `Success` = 2xx · `ClientErrors` = 4xx **excluding 429** · `Throttled429` = 429 · `ServerErrors` = 5xx ·
       `Total` = sum of all
     - `AzureOpenAITimeToResponse` → `AvgMs` (Average aggregation) · `MaxMs` (Maximum aggregation)
   - The `UsageRange` maps to the Azure Monitor query timespan (`Last24h` → 24h, etc.); the resolved
     `From`/`To` are echoed back.

3. **Pure cost calculator** (`Usage/UsageCostCalculator` or similar — pure, no I/O) reads the price table
   from config `Pricing:` and enriches `UsageData` into the response, computing `estimatedCostUsd` per
   deployment and in totals:
   - `Pricing` config shape (per deployment, **non-secret**, USD per 1K tokens):
     ```jsonc
     "Pricing": {
       "gpt-5.4-mini":            { "InputPer1K": 0.00015, "OutputPer1K": 0.0006 },
       "text-embedding-3-small":  { "InputPer1K": 0.00002, "OutputPer1K": 0.0 }
     }
     ```
     *(assumption: the example rates above are placeholders; real rates are operator-set config, not
     committed truth.)*
   - Per deployment: `estimatedCostUsd = PromptTokens/1000 * InputPer1K + CompletionTokens/1000 * OutputPer1K`.
     Totals' cost = sum of per-deployment costs.
   - A deployment with **no `Pricing` entry contributes $0** to cost (and the value stays an honest
     "estimate") — it is not an error. *(assumption flagged so the UI can footnote "deployments without a
     configured price are excluded from the estimate".)*

4. **Offline/test adapter `InMemoryUsageSource : IUsageSource`** — returns canned `UsageData` seeded for
   tests; honors `range` (a narrower range yields a strict subset/zeros for the canned data); an empty
   window yields all-zero aggregates. This is the provider pinned in CI (no Azure Monitor access).

5. **`GET /usage?range=24h|7d|30d`** (default `24h` when omitted):
   - Parses `range`; an **unrecognized value → `400` `ProblemDetails`** (validate/throw early). Valid
     request → `200` with the JSON below.
   - Composes `IUsageSource.GetUsageAsync` + `UsageCostCalculator` and serializes:
     ```jsonc
     {
       "range": "24h",
       "from": "2026-06-08T12:00:00Z",
       "to":   "2026-06-09T12:00:00Z",
       "totals":      { "promptTokens": 0, "completionTokens": 0, "totalTokens": 0, "estimatedCostUsd": 0.0 },
       "deployments": [
         { "deployment": "gpt-5.4-mini", "promptTokens": 0, "completionTokens": 0, "totalTokens": 0, "estimatedCostUsd": 0.0 }
       ],
       "requests":    { "total": 0, "success": 0, "clientErrors": 0, "throttled429": 0, "serverErrors": 0 },
       "latency":     { "avgMs": 0.0, "maxMs": 0.0 }
     }
     ```
   - **An empty / no-data window returns this shape with zeros and `200` — never `500`.**

## What to build — frontend
- **Typed client only.** Add `getUsage(range: UsageRange): Promise<ApiResult<UsageReport>>` to
  `api/client.ts` — the **single `fetch` caller** invariant holds (`fetch-discipline.test.ts` stays
  green). It maps status → `ApiResult` exactly like `ask` / `listDocuments`: `200` → `{ ok: true, value }`;
  `400` → `{ ok: false, error: { kind: 'validation', … } }` (via the existing `errorForStatus`); other
  non-OK → `'server'`; a thrown fetch → `'network'`. No new `ApiErrorKind` is needed. Add the
  `UsageReport` / `UsageRange` TS types mirroring the JSON above (no `any`; explicit return types).
- **New "Ops / Usage" tab.** Extend `type Tab` to include `'usage'` and add the segmented-control button
  (after Dashboard). The view (`ui/usage/UsageView.tsx`) renders, for the selected range:
  - **Totals**: total tokens + estimated cost (with an "estimate" footnote).
  - **Per-deployment table**: deployment · prompt · completion · total tokens · est. cost.
  - **Request-health card**: total / success / 4xx / 429 / 5xx.
  - **Latency card**: avg ms / max ms.
  - A **range selector** (24h / 7d / 30d) that re-queries on change.
  - Explicit **loading**, **error** (shows the `ApiError.detail`), and **empty** (all-zeros) states.
- Presentational components stay free of `fetch`; any non-trivial shaping (e.g. number/cost formatting,
  zero-detection for the empty state) lives in small pure helpers so it's unit-testable under jsdom.

## Constraints
- **Backend / TS conventions** per `CLAUDE.md`: .NET 10 Web API under `/backend`, `async`/`await`
  end-to-end, constructor DI, file-scoped namespaces, `record` DTOs, validate/throw early. React 19 +
  Vite SPA under `/frontend`, `strict`, function components, explicit return types, no `any`.
- **New port, mirroring the established seam pattern** (ADR-0017 / ADR-0018): `UsageSource:Provider`
  selects `azuremonitor` (live default in `appsettings.json`) or `inmemory` (offline/test). **Tests pin
  `UsageSource:Provider=inmemory` assembly-wide via `TestModuleInitializer`** (alongside the existing
  `VectorStore__Provider` / `DocumentStore__Provider` pins) so CI — which has no Azure Monitor access —
  stays green. This is the green-locally / red-in-CI trap ADR-0017 warns about; the pin is the guard.
- **New NuGet dependency: `Azure.Monitor.Query`** (regenerate the `packages.lock.json` files — a stale
  lock blocks `--locked-mode` CI restore). No other new packages.
- **No new secret.** `Monitor:ResourceId` and the `Pricing:` table are **non-secret** config (document
  them in `AZURE-CONFIG.md`); auth is managed identity via `DefaultAzureCredential`. The live provider
  requires the **Monitoring Reader** role grant on the Foundry resource (ADR-0020; AZURE-CONFIG §6.5 /
  §9) — a deploy-time infra step, not code.
- **Existing contracts are UNTOUCHED.** `IVectorStore`, `IDocumentStore`, and the `/ask` + `/documents`
  surfaces do not change. This feature is purely additive (one port, one endpoint, one tab).
- **Cost is an estimate** computed from config, surfaced honestly in the UI. Real billing-grade dollars
  (Azure Cost Management) are explicitly **not** in this feature.
- **Out of scope:** per-request logs · Log Analytics / KQL · **per-feature attribution** (`/ask` vs.
  extraction vs. embedding — platform metrics can't, by ADR-0020; would need App Insights OTel) · Azure
  Cost Management dollars · alerting · **historical persistence** (the dashboard reads live from Azure
  Monitor on each load; nothing is stored). Auth/RBAC on the endpoint is out of scope (consistent with
  the slice).

## How to verify
TDD — tests written first, the suite green before done.

- **`InMemoryUsageSource` (unit):** returns the canned aggregates; `range` filtering behaves (narrower
  range → strict subset / zeros); an empty window → all-zero `UsageData` (no throw).
- **Cost calculator (unit):** `tokens × price table = expected USD`, computed per deployment with input
  and output rates applied **separately**, and totals = sum of per-deployment costs; a deployment with
  **no price entry → $0** (not an error); zero tokens → $0.
- **Endpoint (integration, `WebApplicationFactory`, `UsageSource:Provider=inmemory`):**
  - `GET /usage` → `200` with the **documented JSON shape** (totals, deployments[], requests, latency,
    echoed range/from/to).
  - `?range=7d` / `?range=30d` parsed; **omitted → 24h**; **invalid `range` → `400` `ProblemDetails`**.
  - A no-data window → **zeros with `200`, never `500`**.
- **Frontend (Vitest + RTL, mocked client):**
  - `getUsage` maps `200`/`400`/`5xx`/network → `ApiResult` exactly like `ask`/`listDocuments`
    (and `fetch-discipline.test.ts` stays green — only `client.ts` calls `fetch`).
  - The Ops / Usage view renders totals + the per-deployment table + the request/latency cards from a
    mocked success; the **range selector** re-queries on change; and the **loading**, **error**, and
    **empty (all-zeros)** states each render.
- **Invariants & build:** `dotnet build` + `dotnet test` green (new tests included; lock files
  regenerated so `--locked-mode` restore passes); `npm run typecheck`, `npm test`, `npm run build` green.

## Links
- **Realizes:** [[knowledge/docs/decisions/0020-llm-usage-cost-observability-azure-monitor-metrics]]
  (Azure Monitor platform metrics as the source, behind `IUsageSource`; cost computed; the honest-estimate
  and no-per-feature-attribution consequences).
- **Builds on:** [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]
  and ADR-0012 (the Azure OpenAI deployments being measured) ·
  [[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]] (managed-identity
  credential) · [[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]] (the
  config-selected-provider seam pattern this mirrors). Frontend rides the single-typed-client pattern from
  [[knowledge/docs/specs/0003-frontend-vertical-slice]]; distinct from the analyst dashboard of
  [[knowledge/docs/specs/0007-insights-dashboard-and-document-search-export]].
- **Docs to reconcile on merge:** `API.md` (add `GET /usage` + its shape and error model) ·
  `AZURE-CONFIG.md` (`Monitor:ResourceId` + `Pricing:` keys + the Monitoring Reader role grant — §6.5/§9
  already seeded by ADR-0020) · `ARCHITECTURE.md` / `DATA-FLOW.md` (the `IUsageSource` seam + the `/usage`
  read path) · `DEPLOYMENT.md` (the Monitoring Reader grant step) · `STACK.md` (`Azure.Monitor.Query` —
  row already seeded by ADR-0020, pin its version on build) · README feature line.
- **Implementing PR:** _TBD_ (on `feat/llm-dashboard`).
