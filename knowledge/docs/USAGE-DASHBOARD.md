# LLM Usage & Cost dashboard — how it works, what it needs, how to run it

The **Ops / Usage** tab shows LLM token usage, **estimated** cost, request health, and latency for the
Foundry-hosted Azure OpenAI deployments. This is the operator/dev guide: how the data flows, exactly what
configuration it needs (and what is **not** a secret), what was wired in Azure, and how to run it locally.

Design of record: [spec 0009](specs/0009-llm-usage-and-cost-ops-dashboard.md) ·
[ADR-0020](decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md). The sequence diagram lives
in [DATA-FLOW.md](DATA-FLOW.md#usage--llm-ops-dashboard); the endpoint contract in [API.md](API.md#get-usage).

## How it works (in one breath)
SPA **Ops / Usage** tab → `GET /usage?range=24h|7d|30d` → the config-selected **`IUsageSource`** returns raw
aggregates → a pure **`UsageCostCalculator`** adds estimated cost from a price table → JSON back to the SPA.

- **Live source** (`AzureMonitorUsageSource`) reads **Azure Monitor platform metrics** for the Foundry
  resource via `Azure.Monitor.Query` + **managed identity** (`DefaultAzureCredential`) — `ProcessedPromptTokens`,
  `GeneratedTokens`, `AzureOpenAIRequests` (split by `StatusCode`), and `Latency`, split by
  `ModelDeploymentName`. Read-only, free, no persistence (it reads live each call; no stored history).
  > **Resource-kind note:** this is an **AIServices** (Foundry) account, so latency is the `Latency` metric
  > (the classic `AzureOpenAITimeToResponse` named in spec 0009 / ADR-0020 doesn't exist on it), and Azure
  > returns split-dimension keys **lower-cased** (`modeldeploymentname` / `statuscode`) — the adapter matches
  > them **case-insensitively** (`MetricMetadata.TryGetDimension`). Verified live 2026-06-09.
- **Offline source** (`InMemoryUsageSource`) returns canned aggregates — no Azure, no creds.
- **Cost is computed, not measured**: tokens × a configured per-1K price table. It's an **estimate**, not
  the Azure invoice. A deployment with no configured price contributes $0.
- A **no-data window returns zeros with `200`** (never `500`); an unrecognized `range` → `400`.

## Configuration keys — and what is NOT a secret
**No Key Vault secrets were added for this feature.** Auth is the app's **managed identity** (the same one
already used for Key Vault + Blob), so there is no key, connection string, or token to store. The two config
keys it introduces are **non-secret** and live in `appsettings.json` / plain env vars:

| Key (`:` = `__` in env) | Secret? | Default (committed) | What it does |
|---|---|---|---|
| `UsageSource:Provider` | no | `azuremonitor` | Selects the adapter: `azuremonitor` (live) or `inmemory` (offline/test). |
| `Monitor:ResourceId` | no | *empty* | The Foundry resource id whose metrics are read. **Required** when `azuremonitor` — the adapter throws fast if it's empty. |
| `Pricing:<deployment>:InputPer1K` / `:OutputPer1K` | no | example rates | USD per 1,000 tokens, per deployment, used to compute estimated cost. Override for real dollars. |

Tests/CI need none of this — `TestModuleInitializer` pins `UsageSource__Provider=inmemory`, so the suite is
fully offline and CI stays green.

## What was wired in Azure (as-built — 2026-06-09)
Applied to the live environment (subscription `c3ef00c0-…`, RG `rg-landdoc-deomo`). Procedure:
[DEPLOYMENT.md §1g](DEPLOYMENT.md); inventory/state: [AZURE-CONFIG.md §6.5/§9](AZURE-CONFIG.md).

1. **Role grant (read-only, least privilege):** the Container App `landdoc`'s system-assigned managed
   identity (principal `<MI_PRINCIPAL_ID>`) was granted **`Monitoring Reader`** on the
   Foundry resource **`landdoc-rag-resource`** (kind `AIServices` — it hosts the `gpt-5.4-mini` chat and
   `text-embedding-3-small` deployments, so it's the resource that emits the token/request/latency metrics).
2. **App config:** `Monitor__ResourceId` was set on the Container App to that resource's id
   (`/subscriptions/c3ef00c0-…/resourceGroups/rg-landdoc-deomo/providers/Microsoft.CognitiveServices/accounts/landdoc-rag-resource`).
   It's a non-secret env var (not a Key Vault entry) and **persists across redeploys**.

Nothing else changed — no new resource, no Key Vault secret, no Foundry-side setting beyond the role
assignment on its scope. **Cost:** $0 (RBAC + an env var).

> The endpoint goes live when the feature ships to `main` (CI/CD redeploys). The Azure wiring above is
> already in place, so once the new image is deployed the dashboard works without further Azure steps.

## Running it locally — does it "just work"?
**Not with the committed defaults.** `appsettings.json` ships `UsageSource:Provider=azuremonitor` with an
**empty `Monitor:ResourceId`**, so a local `GET /usage` would throw (→ 500) until you pick one of these:

- **Offline (recommended for UI work / demos)** — no Azure, no credentials:
  ```bash
  cd backend
  UsageSource__Provider=inmemory dotnet run --project src/LandDoc.Api
  ```
  The dashboard renders fully with canned per-deployment data.

- **Live data from your machine** — read the real metrics as *your* identity (`DefaultAzureCredential` uses
  your `az login` locally; as a subscription Owner/Contributor/Reader you already have metric-read access, so
  no extra grant is needed):
  ```bash
  cd backend
  az login
  Monitor__ResourceId="/subscriptions/<SUBSCRIPTION_ID>/resourceGroups/rg-landdoc-deomo/providers/Microsoft.CognitiveServices/accounts/landdoc-rag-resource" \
    dotnet run --project src/LandDoc.Api
  # (UsageSource:Provider already defaults to azuremonitor)
  ```
  The frontend (`npm run dev`) proxies `/usage` to this backend, so the tab shows live numbers.

Frontend-only work needs nothing special: `UsageView` calls the typed client, and component tests mock it.

## What to expect (honest caveats)
- **Zeros are normal.** Metrics only populate from *actual* model calls; an idle window shows zeros (200, not
  an error), and there's ~1–3 min ingestion lag before recent calls appear.
- **Cost uses example rates** until you set real `Pricing:` values — it's labeled an estimate in the UI.
- **Live path verified 2026-06-09** against `landdoc-rag-resource` — real tokens, per-deployment split,
  request buckets, and latency all populate. CI still pins `inmemory`, so the live metric/dimension names
  aren't exercised by tests; if Azure changes a metric name or another resource kind emits different names,
  that series would read 0 rather than erroring (the names are documented above).

## Future upgrades (behind the same `IUsageSource` port — ADR-0020)
- **Application Insights OpenTelemetry GenAI traces** → per-app-feature attribution (`/ask` vs. extraction vs.
  embedding), which platform metrics can't provide.
- **Azure Cost Management API** → billing-grade dollars as a cross-check against the computed estimate.
