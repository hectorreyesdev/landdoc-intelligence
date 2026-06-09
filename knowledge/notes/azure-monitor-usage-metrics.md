# Azure Monitor usage metrics (consuming them in code)

How the LLM **Ops / Usage** dashboard reads token/cost/latency telemetry, and the gotchas I hit doing it.
Design: [[knowledge/docs/decisions/0020-llm-usage-cost-observability-azure-monitor-metrics]] /
spec 0009. Operator guide: `knowledge/docs/USAGE-DASHBOARD.md`. Related: [[azure-deployment]].

## The approach
Read **platform metrics** (free, ~93-day, 1-min grain, emitted automatically) for the Foundry resource via
`Azure.Monitor.Query` `MetricsQueryClient` + `DefaultAzureCredential` (managed identity in hosting, `az login`
locally). Auth needs only **Monitoring Reader** (read-only) on the resource — **no secret**. The resource id
(`Monitor:ResourceId`) and the per-1K **price table** (`Pricing:`) are **non-secret** config (env/appsettings,
not Key Vault). Cost is **computed** (tokens × table) — an estimate, not the invoice.

## Gotchas (the ones that actually bit)
- **Metric names are resource-kind-specific.** This account is **AIServices** (Foundry), not classic Azure
  OpenAI. Latency is **`Latency`**, *not* `AzureOpenAITimeToResponse` (which doesn't exist here → read 0).
  Tokens: `ProcessedPromptTokens` / `GeneratedTokens` exist (also `InputTokens`/`OutputTokens`/`TotalTokens`,
  `TokenTransaction`). **Always run `az monitor metrics list-definitions --resource <id>` first** to see the
  real names + their supported dimensions before coding against them.
- **Split-dimension metadata keys come back LOWER-CASED** (`modeldeploymentname`, `statuscode`) and the SDK's
  `MetricTimeSeriesElement.Metadata` is a **case-sensitive** `IReadOnlyDictionary<string,string>`. A CamelCase
  `TryGetValue("ModelDeploymentName")` misses every series → empty splits. **Look up dimensions
  case-insensitively** (`MetricMetadata.TryGetDimension`, OrdinalIgnoreCase).
- **Splitting** is `MetricsQueryOptions.Filter = "Dimension eq '*'"`; without it you get one aggregate series.
- **An aggregate that sums unconditionally masks a split-parsing bug.** Request *Total* showed correctly while
  every per-status / per-deployment value read 0 — because Total summed before the (failing) metadata read.
  Symptom→cause: one metric's total is right but all its splits are 0 ⇒ suspect the **dimension read**, not
  the metric name.
- **CI can't catch any of this** — the suite pins `UsageSource:Provider=inmemory`, so the live adapter is
  unexercised. Verify by running the API against the real resource (`Monitor__ResourceId` + `az login`) and
  curling `/usage`.

## Shape that worked
`GET /usage?range=24h|7d|30d` → `IUsageSource` returns RAW aggregates (tokens/requests/latency, split by
`ModelDeploymentName`; requests also by `StatusCode` → success 2xx / 4xx / 429 / 5xx) → a **pure**
`UsageCostCalculator` adds `estimatedCostUsd` from the price table → JSON. No-data window → zeros + 200, never
500. Cost lives outside the adapter so it's provider-independent and unit-testable.
