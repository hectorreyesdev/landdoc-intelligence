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

## Test
- Backend: `dotnet test`
- Frontend: `npm test`

## Build
- Backend: `dotnet build`
- Frontend: `npm run build`

## Configuration & secrets (names only — never commit secret values)
Provider/model selection and non-secret tuning live in `appsettings.json`; secrets go in
`dotnet user-secrets` (dev) or environment variables (prod would use Azure Key Vault).

Settings (non-secret, in `appsettings.json`):
- `ModelClient:ChatProvider` — `azureopenai` (live default) | `anthropic` (config-swap fallback)
- `ModelClient:EmbeddingProvider` — `azureopenai` (live default) | `local` (offline/test)
- `AzureOpenAI:Deployment` / `AzureOpenAI:EmbeddingDeployment` — Azure deployment names (e.g. `gpt-5.4-mini` / `text-embedding-3-small`)
- `Anthropic:Model` — fallback chat model (default `claude-opus-4-8`)
- `Embedding:Dimension` — embedding vector length, shared by both adapters (Azure honors it via the `dimensions` parameter; default 256)
- `Chunking:MaxChars` / `Chunking:Overlap` — chunk window + overlap (default 800 / 150)
- `Retrieval:TopK` — top-k chunks for `/ask` (default 5)

Secrets (never commit values):
- Azure OpenAI: `AzureOpenAI:Endpoint` + `AzureOpenAI:ApiKey` (one resource serves both chat and embeddings)
- Anthropic: `Anthropic:ApiKey` (fallback chat)

## Deploy
- Out of scope for the slice — no cloud infrastructure is provisioned. Runs locally only.
- Intended prod topology (named, not built): the SPA on **Azure Static Web Apps** with a **linked
  backend** reverse-proxying the API under the SWA origin — single-origin, still no CORS. See
  [ADR-0011](decisions/0011-single-origin-spa-api-topology.md).

## Observability
- Minimal (console logging). The observability stack is explicitly out of scope.

## Teardown (cost-guarded)
- The slice is **local and in-memory**: stop the `dotnet` and `npm` processes and everything is
  gone. **No cloud resources are created, so there is nothing to bill or delete.**
- If you later wire the production path (Azure OpenAI / Azure AI Search / Key Vault), delete those
  resource groups afterward to avoid ongoing charges.
