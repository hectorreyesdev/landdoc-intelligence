# Runbook

> Both `/backend` and `/frontend` are scaffolded — the commands below are real (run `dotnet` from
> `backend/`, `npm` from `frontend/`). Mirrors `CLAUDE.md` → Build / test / run.

## Prerequisites
- **.NET 10 SDK** (LTS).
- **Node.js** LTS + npm.
- A model provider credential: an **Azure OpenAI** endpoint + key (the live default, serving both chat
  and embeddings) **or** an Anthropic API key for chat (then pin `EmbeddingProvider=local`).

## Install
- Backend: `dotnet restore` (in `/backend`).
- Frontend: `npm install` (in `/frontend`).

## Run
- Backend: `dotnet run --project src/LandDoc.Api`
- Frontend: `npm run dev` — the Vite dev server proxies `/documents` and `/ask` to the backend so the
  browser stays **single-origin** (no CORS); the typed client calls relative paths. See
  [ADR-0011](decisions/0011-single-origin-spa-api-topology.md).

## Run as a single container (prod-shape)
The repo-root `Dockerfile` builds **one image** that serves the built SPA *and* the API from one
origin on port **8080** (same-origin, no CORS) — the shape deployed to Azure Container Apps.
Requires **Docker**.

```bash
# from the repo root
docker build -t landdoc .

# run it — pass model config/secrets as env vars (NOT baked into the image)
docker run --rm -p 8080:8080 \
  -e ModelClient__EmbeddingProvider=local \
  -e ModelClient__ChatProvider=anthropic \
  -e Anthropic__ApiKey="$ANTHROPIC_API_KEY" \
  landdoc
```

Then open <http://localhost:8080> — the SPA, the upload→extract→ask flow, and any deep-link/refresh
all work from that one origin.

- Config maps to env vars by replacing `:` with `__` (e.g. `ModelClient:ChatProvider` →
  `ModelClient__ChatProvider`). See **Configuration & secrets** below for all keys.
- The combo above (`EmbeddingProvider=local` + `ChatProvider=anthropic`) needs only an Anthropic key.
  For the full Azure path, set `ModelClient__EmbeddingProvider=azureopenai`,
  `ModelClient__ChatProvider=azureopenai`, `AzureOpenAI__Endpoint`, and `AzureOpenAI__ApiKey` instead.
- Without any provider credential the app starts and serves the SPA, but `POST /ask` / `POST /documents`
  return a 500 when they reach the model — expected; no secrets are baked into the image.

### Pulling secrets from Azure Key Vault (no keys in env or image)
Instead of passing keys, point the app at the vault. `DefaultAzureCredential` authenticates as your
`az login` locally (run with `dotnet run`, since the container can't see your CLI session) and as the
**managed identity** in Azure Container Apps. The identity needs the **`Key Vault Secrets User`** role
on the vault (RBAC-mode).

```bash
# local, against the real vault (uses your `az login`):
cd backend
KeyVault__Uri="https://kv-landdoc-hr01.vault.azure.net/" \
  dotnet run --project src/LandDoc.Api --no-launch-profile
# in the container you'd instead authenticate via a managed identity (see Deploy) — not `az login`.
```
With the vault wired, `POST /ask` reaches the embedding model using the vault-supplied key (returns
409 *empty store* until you ingest a document, not a no-credential 500).

Smoke-check routing (in another terminal):
```bash
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:8080/          # 200 text/html (SPA)
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:8080/any/route  # 200 text/html (SPA fallback)
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:8080/documents          # 400 (reaches the API)
```

## Test
- Backend: `dotnet test`
- Frontend: `npm test`

## Build
- Backend: `dotnet build`
- Frontend: `npm run build`

## Configuration & secrets (names only — never commit secret values)
Provider/model selection and non-secret tuning live in `appsettings.json`; secrets go in
`dotnet user-secrets` (dev) or environment variables (prod would use Azure Key Vault).

Secrets can be supplied three ways, in increasing order of "prod-like": `dotnet user-secrets` (dev),
plain env vars (`AzureOpenAI__ApiKey` etc.), or **Azure Key Vault** (set `KeyVault:Uri` — see below).

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
- `UsageSource:Provider` — `azuremonitor` (live default — Azure Monitor platform metrics for the LLM
  usage/cost dashboard) | `inmemory` (offline/test; pinned by `TestModuleInitializer`). See [ADR-0020](decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md)
- `Monitor:ResourceId` — the Foundry resource id read for usage metrics (managed-identity auth; **non-secret**,
  not a Key Vault entry). Required when `UsageSource:Provider=azuremonitor` — the adapter throws fast if unset.
  Needs the **Monitoring Reader** role on that resource (see [DEPLOYMENT.md §1g](DEPLOYMENT.md)).
- `Pricing:<deployment>:InputPer1K` / `Pricing:<deployment>:OutputPer1K` — **non-secret** per-deployment price
  table (USD per 1K tokens) used to **compute** estimated cost; a deployment with no entry contributes $0.
- `KeyVault:Uri` — when set, Key Vault is added as a config source via `DefaultAzureCredential`; secrets
  named `AzureOpenAI--ApiKey` / `AzureOpenAI--Endpoint` / `Anthropic--ApiKey` / `Search--Endpoint` /
  `Search--ApiKey` / `Blob--ConnectionString` overlay
  the matching keys (`--` → `:`). Unset → vault is skipped (offline/test). See [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).
- `AzureOpenAI:Deployment` / `AzureOpenAI:EmbeddingDeployment` — Azure deployment names (e.g. `gpt-5.4-mini` / `text-embedding-3-small`)
- `Anthropic:Model` — fallback chat model (default `claude-opus-4-8`)
- `Embedding:Dimension` — embedding vector length, shared by both adapters (Azure honors it via the `dimensions` parameter; default 256)
- `Chunking:MaxChars` / `Chunking:Overlap` — chunk window + overlap (default 800 / 150)
- `Retrieval:TopK` — top-k chunks for `/ask` (default 8)

Secrets (never commit values):
- Azure OpenAI: `AzureOpenAI:Endpoint` + `AzureOpenAI:ApiKey` (one resource serves both chat and embeddings)
- Anthropic: `Anthropic:ApiKey` (fallback chat)
- Azure AI Search: `Search:Endpoint` + `Search:ApiKey` (admin key; Free tier has no managed identity — ADR-0017)
- Azure Blob: `Blob:ConnectionString` (only if not using the managed-identity `Blob:ServiceUri` path — ADR-0018)

## Deploy
- Provisioning cloud infra is out of scope for the slice — it runs locally only. But the app is now
  **container-ready**: the repo-root `Dockerfile` produces a single image (see *Run as a single
  container* above).
- Prod topology (image built; infra provisioned on demand): run the single image on **Azure Container
  Apps** — one container serving the SPA and API on one origin, port 8080, still single-origin and no
  CORS. Secrets come from **Key Vault via the Container App's managed identity**, never the image. See
  [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).
- Deploy outline (Azure CLI; `containerapp` extension):
  1. `az containerapp up` builds the image (creates an ACR + Container Apps environment on first run),
     deploys it, and returns a public FQDN. Set `--target-port 8080 --ingress external` and env vars
     `KeyVault__Uri`, `ModelClient__ChatProvider`, `ModelClient__EmbeddingProvider`.
  2. Enable a **system-assigned managed identity** on the Container App
     (`az containerapp identity assign --system-assigned`).
  3. Grant it **`Key Vault Secrets User`** on the vault scope
     (`az role assignment create --role "Key Vault Secrets User" --assignee <principalId> --scope <vault resourceId>`).
  4. The app reads `AzureOpenAI--*` / `Anthropic--*` from the vault at startup — no keys on the
     Container App. Restart if the role was granted after first boot.

## Observability
- App logging is minimal (console), and the **infra** observability stack (Log Analytics ingestion, App
  Insights traces, alerting) is out of scope.
- The one in-scope exception is the **in-app LLM usage dashboard**: `GET /usage` reads free, read-only
  **Azure Monitor platform metrics** for the Foundry resource (ADR-0020). In Azure it needs the **Monitoring
  Reader** role + `Monitor:ResourceId` (DEPLOYMENT §1g; already wired — AZURE-CONFIG §6.5).
- **Locally it does *not* just work with committed defaults** (`UsageSource:Provider=azuremonitor` + empty
  `Monitor:ResourceId` → `/usage` 500s). Either pin `UsageSource__Provider=inmemory` for canned data (no
  Azure, no creds), or set `Monitor__ResourceId` + `az login` (your identity already has metric-read). Full
  walkthrough — how it works, keys, local modes: **[USAGE-DASHBOARD.md](USAGE-DASHBOARD.md)**.

## Teardown (cost-guarded)
- The slice is **local and in-memory**: stop the `dotnet` and `npm` processes (or the container —
  `--rm` removes it on exit; otherwise `docker rm -f <name>` and `docker rmi landdoc`) and everything
  is gone. **No cloud resources are created, so there is nothing to bill or delete.**
- If you later wire the production path (Azure OpenAI / Azure AI Search / Key Vault), delete those
  resource groups afterward to avoid ongoing charges.
