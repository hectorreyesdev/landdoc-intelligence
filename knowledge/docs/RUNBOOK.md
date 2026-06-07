# Runbook

> `/backend` is scaffolded — the backend commands below are real (run `dotnet` from `backend/`).
> `/frontend` is not scaffolded yet, so the `npm` commands remain the intended workflow. Mirrors
> `CLAUDE.md` → Build / test / run.

## Prerequisites
- **.NET 10 SDK** (LTS).
- **Node.js** LTS + npm.
- A chat provider credential: a Microsoft Foundry endpoint/key **or** an Anthropic API key.

## Install
- Backend: `dotnet restore` (in `/backend`).
- Frontend: `npm install` (in `/frontend`).

## Run
- Backend: `dotnet run --project src/LandDoc.Api`
- Frontend: `npm run dev`

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
- `ModelClient:ChatProvider` — `foundry` | `anthropic`
- `ModelClient:EmbeddingProvider` — `local` | `foundry`
- `ModelClient:ChatModel` — e.g. `claude-opus-4-8`
- `Embedding:Dimension` — local embedding vector length (default 256)
- `Chunking:MaxChars` / `Chunking:Overlap` — chunk window + overlap (default 120 / 30)
- `Retrieval:TopK` — top-k chunks for `/ask` (default 5; read path not built yet)

Secrets (never commit values):
- Foundry: endpoint + key — TODO confirm key names (e.g. `Foundry:Endpoint`, `Foundry:ApiKey`)
- Anthropic: `ANTHROPIC_API_KEY`
- Azure OpenAI embeddings (prod, out of scope): endpoint / deployment / key

## Deploy
- Out of scope for the slice — no cloud infrastructure is provisioned. Runs locally only.

## Observability
- Minimal (console logging). The observability stack is explicitly out of scope.

## Teardown (cost-guarded)
- The slice is **local and in-memory**: stop the `dotnet` and `npm` processes and everything is
  gone. **No cloud resources are created, so there is nothing to bill or delete.**
- If you later wire the production path (Azure OpenAI / Azure AI Search / Key Vault), delete those
  resource groups afterward to avoid ongoing charges.
