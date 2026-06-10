# Runbook — index

Operational docs for LandDoc Intelligence. This page is the entry point + the canonical
**Configuration & secrets** reference; the task-specific runbooks below own the actual steps.

| If you want to… | Go to |
|---|---|
| Run & debug on your machine (dev, container, tests) | [RUNBOOK-LOCAL.md](RUNBOOK-LOCAL.md) |
| Operate the live Azure environment (deploy, logs, rollback, scale) | [RUNBOOK-PROD.md](RUNBOOK-PROD.md) |
| Full first-time Azure provisioning (CLI, custom domain, teardown) | [DEPLOYMENT.md](DEPLOYMENT.md) |
| The auto-deploy-on-merge pipeline (GitHub Actions + OIDC) | [CICD.md](CICD.md) |

**TL;DR**
- **Local:** `dotnet run --project src/LandDoc.Api` (`:5084`) + `npm run dev` (`:5173`, proxies the API).
- **Prod:** merging a PR to `main` auto-deploys (CD); manual is `az containerapp up --source .`.
- **Gates:** `dotnet test` · `npm test` · `npm run build` · `npm ci`.

## Configuration & secrets
_Names only — never commit secret values._ Provider/model selection and non-secret tuning live in `appsettings.json`; secrets go in
`dotnet user-secrets` (dev), plain env vars, or **Azure Key Vault** (prod — set `KeyVault:Uri`).
Config maps to env vars by replacing `:` with `__` (e.g. `ModelClient:ChatProvider` → `ModelClient__ChatProvider`).

Settings (non-secret, in `appsettings.json`):
- `ModelClient:ChatProvider` — `azureopenai` (live default) | `anthropic` (config-swap fallback)
- `ModelClient:EmbeddingProvider` — `azureopenai` (live default) | `local` (offline/test)
- `VectorStore:Provider` — `azuresearch` (live default) | `inmemory` (offline/test; tests pin this via
  `TestModuleInitializer` so CI without Search creds stays green). See [ADR-0017](decisions/0017-azure-ai-search-free-tier-live-vector-store.md)
- `Search:Endpoint` / `Search:ApiKey` — Azure AI Search service URL (`.search.windows.net`) + admin key
  (Free tier, eastus; key auth — no managed identity, ADR-0017); index `landdoc-chunks` (created on startup)
- `DocumentStore:Provider` — `azureblob` (live default) | `inmemory` (offline/test; pinned by
  `TestModuleInitializer`). See [ADR-0018](decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata.md)
- `Blob:ServiceUri` — Blob endpoint (`https://stlanddochr01.blob.core.windows.net`); when set, auth is via
  managed identity (`DefaultAzureCredential`). Else falls back to `Blob:ConnectionString`. `Blob:ContainerName`
  defaults to `documents`. For local dev without Azure, set `DocumentStore:Provider=inmemory`.
- `KeyVault:Uri` — when set, Key Vault is added as a config source via `DefaultAzureCredential`; secrets
  named `AzureOpenAI--ApiKey` / `AzureOpenAI--Endpoint` / `Anthropic--ApiKey` / `Search--Endpoint` /
  `Search--ApiKey` / `Blob--ServiceUri` (or `Blob--ConnectionString`) overlay the matching keys (`--` → `:`).
  Unset → vault is skipped (offline/test). See [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).
- `AzureOpenAI:Deployment` / `AzureOpenAI:EmbeddingDeployment` — Azure deployment names (e.g. `gpt-5.4-mini` / `text-embedding-3-small`)
- `Anthropic:Model` — fallback chat model (default `claude-opus-4-8`)
- `Embedding:Dimension` — embedding vector length, shared by both adapters (Azure honors it via the `dimensions` parameter; default 256)
- `Chunking:MaxChars` / `Chunking:Overlap` — chunk window + overlap (default 800 / 150)
- `Retrieval:TopK` — top-k chunks for `/ask` (default 8)
- `Auth:Mode` — `none` (default — local dev, offline, tests) | `easyauth` (live: every request must carry
  an allowlisted Easy Auth principal; unknown mode or `easyauth` with an empty allowlist fails startup)
- `Auth:AllowedPrincipalIds` — Entra object IDs admitted when `Mode=easyauth` (not secrets; live value is
  the owner's object id, set as the `Auth__AllowedPrincipalIds__0` env var). See
  [ADR-0022](decisions/0022-single-user-entra-auth-easy-auth-gate-app-level-allowlist.md) /
  [spec 0013](specs/0013-single-user-auth-easy-auth-gate-app-allowlist.md)

Secrets (never commit values):
- Azure OpenAI: `AzureOpenAI:Endpoint` + `AzureOpenAI:ApiKey` (one resource serves both chat and embeddings)
- Anthropic: `Anthropic:ApiKey` (fallback chat)
- Azure AI Search: `Search:Endpoint` + `Search:ApiKey` (admin key; Free tier has no managed identity — ADR-0017)
- Azure Blob: `Blob:ConnectionString` (only if not using the managed-identity `Blob:ServiceUri` path — ADR-0018)
- Easy Auth client secret: lives as the **Container App secret** `microsoft-provider-authentication-secret`
  (consumed by the platform auth sidecar, not app config — ADR-0022; expires, rotation in [RUNBOOK-PROD.md](RUNBOOK-PROD.md))

## Observability
Console logging (streamed via `az containerapp logs show` in prod — see [RUNBOOK-PROD.md](RUNBOOK-PROD.md)),
plus LLM usage/cost from **Azure Monitor platform metrics** on the Foundry resource (free, no app code —
[ADR-0020](decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md)). A fuller app-level
observability stack (tracing/APM) is out of scope.
