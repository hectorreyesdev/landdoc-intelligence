# Runbook — run & debug locally

How to run LandDoc Intelligence on your machine and debug it. For the live Azure environment see
[RUNBOOK-PROD.md](RUNBOOK-PROD.md); the full config-key reference lives in
[RUNBOOK.md § Configuration & secrets](RUNBOOK.md#configuration--secrets).

## Prerequisites
- **.NET 10 SDK** (LTS) — `dotnet --version` ≥ 10.
- **Node.js** LTS + npm — `node --version`.
- **Docker** — only for the single-container prod-shape run (optional).
- **Azure CLI** + `az login` — only if you want to run against the live providers / Key Vault (optional).
- A model credential, unless you stub chat: an **Azure OpenAI** endpoint + key (live default; serves chat
  *and* embeddings) **or** an **Anthropic** API key (then pin `EmbeddingProvider=local`).

## Topology & ports (single-origin, ADR-0011)
| Piece | URL | Notes |
|---|---|---|
| Backend API | `http://localhost:5084` (https `7288`) | `ASPNETCORE_ENVIRONMENT=Development`; from `launchSettings.json` |
| Vite dev server | `http://localhost:5173` | proxies `/documents` + `/ask` → `:5084`, so the browser is single-origin (no CORS) |

The proxy target (`API_TARGET` in `frontend/vite.config.ts`) is the **only** place the absolute backend
URL appears; the typed client always uses relative paths.

## Install
```bash
cd backend  && dotnet restore
cd frontend && npm install
```

## Run (dev, two processes)
```bash
# terminal 1 — API
cd backend && dotnet run --project src/LandDoc.Api

# terminal 2 — SPA (Vite HMR)
cd frontend && npm run dev      # open http://localhost:5173
```

### Run modes — pick by how much cloud you want
Config maps env vars by replacing `:` with `__` (e.g. `ModelClient:ChatProvider` → `ModelClient__ChatProvider`).

1. **Fully offline (no Azure, no Search/Blob creds)** — in-memory stores + local embeddings; chat still
   needs one key. Pin the offline providers and pass an Anthropic key:
   ```bash
   cd backend
   ModelClient__EmbeddingProvider=local \
   ModelClient__ChatProvider=anthropic \
   VectorStore__Provider=inmemory \
   DocumentStore__Provider=inmemory \
   Anthropic__ApiKey="$ANTHROPIC_API_KEY" \
     dotnet run --project src/LandDoc.Api
   ```
   The corpus is in-memory: it resets on every restart (by design).

2. **Live providers via Key Vault** — uses real Azure OpenAI + Azure AI Search + Blob, with secrets pulled
   from the vault as your `az login` identity (the container's managed identity isn't visible locally, so
   run with `dotnet run`, not the container):
   ```bash
   cd backend
   az login
   KeyVault__Uri="https://kv-landdoc-hr01.vault.azure.net/" \
     dotnet run --project src/LandDoc.Api --no-launch-profile
   ```
   Your identity needs **Key Vault Secrets User** on the vault and (for blobs) **Storage Blob Data
   Contributor** on `stlanddochr01`. `POST /ask` returns `409` (empty store) until you ingest, not a
   no-credential `500`.

3. **Dev secrets instead of env vars** — `dotnet user-secrets` keeps keys out of your shell history:
   ```bash
   cd backend/src/LandDoc.Api
   dotnet user-secrets set "AzureOpenAI:ApiKey" "<key>"
   dotnet user-secrets set "AzureOpenAI:Endpoint" "<https://…openai.azure.com/>"
   ```

## Run as a single container (prod-shape, optional)
The repo-root `Dockerfile` builds **one image** that serves the built SPA *and* the API on one origin,
port 8080 — the shape deployed to Azure.
```bash
docker build -t landdoc .            # from the repo root
docker run --rm -p 8080:8080 \
  -e ModelClient__EmbeddingProvider=local \
  -e ModelClient__ChatProvider=anthropic \
  -e VectorStore__Provider=inmemory \
  -e DocumentStore__Provider=inmemory \
  -e Anthropic__ApiKey="$ANTHROPIC_API_KEY" \
  landdoc                            # open http://localhost:8080
```
Without any provider credential the app still starts and serves the SPA, but `POST /ask` / `POST /documents`
return `500` when they reach the model — expected; no secrets are baked into the image.

## Smoke-check routing
```bash
BASE=http://localhost:5084           # or :8080 for the container
curl -s -o /dev/null -w "%{http_code} %{content_type}\n" "$BASE/documents"   # 200 application/json ([] when empty)
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$BASE/ask" \
  -H "Content-Type: application/json" -d '{"question":"ping"}'               # 409 empty / 200 / 500 (see below)
```

## Test & build (CI parity)
The PR gates are exactly these — run them before pushing.
```bash
cd backend  && dotnet test          # xUnit + WebApplicationFactory
cd frontend && npm test             # Vitest (incl. fetch-discipline)
cd frontend && npm run build        # tsc -b + vite build
cd frontend && npm ci               # lockfile-in-sync gate (frontend-ci)
```
Focused runs while iterating:
```bash
dotnet test --filter "FullyQualifiedName~ChunkRetriever"
npm test -- src/ui/documentSort.test.ts          # one file
npm test -- -t "sorts chunk count"               # by test name
```

## Debugging
- **Backend (breakpoints):** open the repo in VS / VS Code (C# Dev Kit) / Rider and launch
  `src/LandDoc.Api` with the **http** profile, or `dotnet run` and attach to the process. Logs go to the
  console; `ASPNETCORE_ENVIRONMENT=Development` gives developer exception pages.
- **Frontend:** browser devtools + Vite HMR; the typed client lives in `frontend/src/api/client.ts` — set
  breakpoints there to inspect requests. The dashboard charts/map render no DOM under jsdom, so debug those
  in the browser, not tests.
- **Watch tests:** `npm run test:watch` (frontend) re-runs on save.

### Common responses → what they mean
| Response | Meaning |
|---|---|
| `POST /ask` → `500` | No reachable chat/embedding credential (missing key, or KV identity/role not wired). |
| `POST /ask` → `409` | App + model OK, but the vector store is empty — ingest a document first. |
| `POST /documents` → `400` | Reached the API (validation) — routing is fine; check the request body/file. |
| `GET /documents` → `500` | Document store unreachable (live mode: blob identity/role or `Blob:ServiceUri`). |
| CORS error in browser | You bypassed the Vite proxy — hit `:5173` (dev) or `:8080` (container), not `:5084` directly. |
