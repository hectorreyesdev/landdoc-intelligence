# LandDoc Intelligence

An ASP.NET Core (**.NET 10**) Web API + **React/TypeScript** SPA running a retrieval-augmented Q&A
**vertical slice** over land/title documents: ingest a PDF (or `.txt`/`.md`) → extract structured fields
→ chunk → embed → top-k similarity retrieval → answer **with citations**. Each ingested document is
persisted (original file + metadata), so you also get a **documents table** and a **source-file viewer**,
and every citation links back to the document it came from.

It is **deployed** — a single container on Azure Container Apps, secrets pulled from Key Vault via managed
identity, CI/CD on merge to `main` (see [DEPLOYMENT.md](knowledge/docs/DEPLOYMENT.md) +
[ADR-0016](knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets.md)). It is
**not** production-*hardened* — auth/RBAC, observability, VNet/Private Link and the like are deliberately
out of scope (see [CLAUDE.md](CLAUDE.md)).

Senior-level judgment made visible: deliberate scope (build vs. stub), a spec- and ADR-first workflow,
and an agentic process that's part of the deliverable.

## How it works
- **Ingest** (`POST /documents`) — parse PDF / decode text → extract fields (best-effort) → chunk → embed
  → store chunks in the vector store **and** persist the original file + metadata in the document store.
- **Ask** (`POST /ask`) — embed the question → retrieve top-k chunks across the **whole corpus** → answer
  grounded only in those passages, **cite-or-error** (an answer always carries ≥1 citation, or it 409s on
  an empty store).
- **Read back** (`GET /documents`, `GET /documents/{id}`, `GET /documents/{id}/file`) — list documents with
  their fields, and open the original file inline in the viewer.
- **Explore** (Dashboard tab) — KPI tiles, documents-by-location and ingest-over-time charts, a
  needs-review list, and lease expirations — all aggregated client-side from `GET /documents`; the
  documents table also adds search + CSV export.
- **Delete** (`DELETE /documents/{id}`) — multi-select removal of documents from both stores (chunks +
  file/metadata); idempotent.

## Ports & adapters — provider choice is config, not code
Every external dependency sits behind an interface with a **live** adapter (production default) and an
**offline** adapter (used by the test suite, no cloud credentials). Swapping is a config-section change,
never a code change.

| Port | Live default | Offline / fallback |
|---|---|---|
| `IChatClient` | Azure OpenAI `gpt-5.4-mini` ([ADR-0012](knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md)) | Anthropic `claude-opus-4-8` (config-swap fallback) |
| `IEmbeddingClient` | Azure OpenAI `text-embedding-3-small`, 256-d ([ADR-0013](knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md)) | Local deterministic hashing (offline/test) |
| `IVectorStore` | Azure AI Search Free tier ([ADR-0017](knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store.md)) | In-memory cosine (offline/test) |
| `IDocumentStore` | Azure Blob Storage ([ADR-0018](knowledge/docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata.md)) | In-memory (offline/test) |

## Quickstart
**Backend** (`backend/`) — ASP.NET Core, .NET 10:
```bash
dotnet build
dotnet test                              # fully offline — pins the in-memory + local adapters
dotnet run --project src/LandDoc.Api     # listens on http://localhost:5084
```
**Frontend** (`frontend/`) — React + TS + Vite:
```bash
npm install
npm run dev                              # http://localhost:5173, dev-proxies /documents + /ask to the API
npm test                                 # Vitest + React Testing Library (mocked client)
```
Run with no cloud credentials by pinning the offline providers (defaults in tests):
`ModelClient__EmbeddingProvider=local`, `ModelClient__ChatProvider=anthropic` (needs an Anthropic key) or
a fake, `VectorStore__Provider=inmemory`, `DocumentStore__Provider=inmemory`. See
[RUNBOOK.md](knowledge/docs/RUNBOOK.md) for all config keys.

## Repo map
- [`CLAUDE.md`](CLAUDE.md) — project constitution (architecture · conventions · guardrails)
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — how to work in this repo + the doc workflow
- `backend/` — ASP.NET Core Web API; modules `Ingestion` · `Extraction` · `Retrieval` · `Qa`, ports/adapters under `Storage/` + `Model/`
- `frontend/` — React/TS SPA (upload → fields → documents table → ask → answer-with-citations → source viewer)
- [`knowledge/`](knowledge/README.md) — living docs, evergreen notes, committed session logs, lessons
  - **Design:** [PRD](knowledge/docs/PRD.md) · [Stack](knowledge/docs/STACK.md) · [Architecture](knowledge/docs/ARCHITECTURE.md) · [Data model](knowledge/docs/DATA-MODEL.md) · [Data flow](knowledge/docs/DATA-FLOW.md) · [API](knowledge/docs/API.md) · [Glossary](knowledge/docs/GLOSSARY.md)
  - **Operate:** [Runbook](knowledge/docs/RUNBOOK.md) · [Deployment](knowledge/docs/DEPLOYMENT.md) · [CI/CD](knowledge/docs/CICD.md) · [Azure config](knowledge/docs/AZURE-CONFIG.md)
  - **Decide:** [ADRs](knowledge/docs/decisions/) · [Specs](knowledge/docs/specs/README.md) · [Lessons](knowledge/lessons.md)

> Docs are authored as **design intent** before code; once code lands, `/wrap` keeps them current.
