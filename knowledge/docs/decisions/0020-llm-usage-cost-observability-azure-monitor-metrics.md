# 0020. LLM usage + cost observability from Azure Monitor platform metrics

- Status: Accepted
- Date: 2026-06-09
- Realized by: [spec 0009](../specs/0009-llm-usage-cost-observability-dashboard.md)
- Builds on: [ADR-0013](0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md), [ADR-0016](0016-single-container-azure-container-apps-keyvault-secrets.md), [ADR-0017](0017-azure-ai-search-free-tier-live-vector-store.md)

## Context
We want an **in-app operations view of LLM usage** — tokens, per-deployment breakdown, estimated
cost, throttling, and latency — for the Azure OpenAI models deployed in **Azure AI Foundry**: the
`gpt-5.4-mini` chat deployment ([[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]])
and the `text-embedding-3-small` embedding deployment
([[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]).
This is the "LLM dashboard" surface — a product/ops view inside the app, **not** the infra
"observability stack" CLAUDE.md lists out of scope. It stays in the slice precisely because the
telemetry it reads is **free, read-only, and already being emitted** — no Log Analytics ingestion,
no App Insights pipeline, no alerting rules to provision or tear down.

The real question is **WHERE the telemetry comes from**, because the options differ sharply in cost
and granularity:
- **Azure Monitor platform metrics** — emitted automatically by the Foundry resource; **free**;
  ~93-day retention; 1-minute grain; queryable via the Metrics API / `Azure.Monitor.Query` SDK.
- **Diagnostic settings → Log Analytics + KQL** — per-request detail, but **paid** ingestion +
  retention, and a workspace to provision.
- **Application Insights OpenTelemetry GenAI traces** — per-app-feature attribution (which feature
  spent the tokens); free under the 5 GB/month grant, but materially more app-side plumbing.
- **Azure Cost Management API** — billing-grade dollars, but resource-grain and lagged 8–24h.

Constraints that shape the choice:
- Managed identity is already the credential story ([[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]]) —
  `DefaultAzureCredential`, passwordless, one identity across contexts. A metrics source that reads
  via that identity adds no new secret.
- The slice is explicitly **not production-hardened** (CLAUDE.md). The bar is "useful, free, and
  honest about its limits," not "billing-accurate, per-feature, alerting-grade."
- The repo's established seam pattern — a config-selected port with a live adapter and an
  in-memory offline/test adapter ([[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]],
  [[knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata]]) —
  is the natural shape for "where does usage telemetry come from."

## Decision
We will **source the LLM dashboard from Azure Monitor platform metrics only, behind a new port**,
and **compute** cost rather than measure it. Four parts:

1. **New port `IUsageSource`** (sibling to `IVectorStore` / `IDocumentStore`), config-selected via
   `UsageSource:Provider`: `azuremonitor` (live) | `inmemory` (offline/test — the default pinned in
   the suite). The port exposes a time-windowed usage query returning per-deployment token, request,
   and latency series plus computed cost — the exact shape is carried by spec 0009.
2. **The live adapter uses `Azure.Monitor.Query`'s `MetricsQueryClient`** against the Foundry
   resource id, authenticating with **managed identity** (`DefaultAzureCredential`) — consistent
   with ADR-0016. The required role is **Monitoring Reader** on the Foundry resource (read-only,
   least privilege). **No new secret:** the Foundry resource id and the price table are non-secret
   config.
3. **Metrics pulled** (split by `ModelDeploymentName`): `ProcessedPromptTokens`, `GeneratedTokens`,
   `TokenTransaction` (total inference tokens), `AzureOpenAIRequests` (split by `StatusCode` for
   success / 4xx / 429 / 5xx — this is the throttling signal), and `AzureOpenAITimeToResponse` (the
   latency signal).
4. **Cost is COMPUTED, not measured** — tokens × a configured per-1K price table (per deployment,
   input/output). This is real-time and free. It is an **estimate**, not the invoice.

Tests pin `UsageSource:Provider=inmemory` assembly-wide via the existing `TestModuleInitializer`
(alongside the `VectorStore__Provider` / `DocumentStore__Provider` pins) so CI — which has no Azure
Monitor access — stays green.

## Consequences
- **$0 of new infrastructure.** RBAC + code only; nothing provisioned, nothing to tear down. The
  metrics already exist whether or not we read them.
- **Read-only, least-privilege identity** — Monitoring Reader grants metric reads and nothing else;
  the dashboard cannot mutate the Foundry resource.
- **Cost is an estimate.** Token-count × a configured price table drifts from the actual invoice
  (no discounts, reservations, or rounding). Honest framing in the UI ("estimated"); the **Azure
  Cost Management API** is the future billing-grade cross-check — its own ADR.
- **Platform metrics CANNOT attribute tokens to an application feature.** The metric `FeatureName`
  dimension is Azure's internal channel, **not** our `/ask` vs. extraction vs. embedding. The
  dashboard can split by *deployment* but not by *our* feature. Per-feature attribution would
  require app-side OpenTelemetry GenAI traces to Application Insights — a future ADR, reachable
  behind this same port.
- **Retention/grain are Azure's, not ours** — ~93-day window, 1-minute grain bound what the
  dashboard can show; longer history would need Log Analytics (paid) behind the same port.
- **The provider default is `azuremonitor` (live), so the production default is part of the test
  surface** — exactly the green-locally / red-in-CI trap ADR-0017 warns about; the
  `TestModuleInitializer` `inmemory` pin is the guard.
- **Production hardening stays behind the same port** — Log Analytics + KQL, App Insights GenAI
  traces, and alerting are all later swaps/additions against `IUsageSource`, not a rewrite.
- New NuGet dependency: `Azure.Monitor.Query`.

## Notes (non-binding)
- Two named future upgrades, both reachable behind `IUsageSource` without a rewrite:
  - **Application Insights OTel GenAI traces** — the path to per-app-feature attribution
    (`/ask` vs. extraction vs. embedding), free under the 5 GB/month grant.
  - **Azure Cost Management API** — billing-grade dollars as a periodic cross-check against the
    computed estimate (resource-grain, 8–24h lagged).
- Spec 0009 carries the port shape, the endpoint contract, the price-table config keys, the dashboard
  views, and the verification plan.
- The `Monitoring Reader` grant on the Foundry resource is the one new infra action — analogous to
  ADR-0018's Storage Blob Data Contributor grant; it belongs in `AZURE-CONFIG.md` / `RUNBOOK.md`.
