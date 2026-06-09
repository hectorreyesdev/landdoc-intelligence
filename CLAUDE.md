# LandDoc Intelligence

AI document-intelligence + retrieval-augmented Q&A over land/title documents (leases, title
opinions, county records). **Vertical slice, NOT production-hardened** — build the simplest thing that
proves the end-to-end flow. It *is* deployed (single container on Azure Container Apps, secrets from Key
Vault via managed identity, CI/CD on merge to `main` — see `knowledge/docs/DEPLOYMENT.md` + ADR-0016), but
production *hardening* (see **Out of scope** below) stays out. Read `knowledge/lessons.md` at the start of
every session (see **Project docs** below).

## Architecture
- **Backend** — ASP.NET Core Web API on **.NET 10 (LTS)**. Modular monolith: one process,
  modules split by folder/namespace (`Ingestion`, `Extraction`, `Retrieval`, `Qa`, `Usage`) — not
  microservices.
- **Model access** — split into TWO interfaces (chat and embeddings have different providers and
  fail over differently):
  - **`IChatClient`** — chat/completions. `AzureOpenAIChatClient` (Azure OpenAI GPT, `gpt-5.4-mini`,
    OpenAI Chat Completions — the **live default**, ADR-0012) + `AnthropicChatClient` (Anthropic API
    direct, **config-swap fallback**, `Anthropic` NuGet SDK). Adapter is **config-only**
    (`ModelClient:ChatProvider`), never a code change.
  - **`IEmbeddingClient`** — embeddings only. `AzureOpenAIEmbeddingClient` (Azure OpenAI
    `text-embedding-3-small`, 256-d — the **live default**, ADR-0013) + `LocalEmbeddingClient`
    (deterministic in-repo hashing — the **offline/test** provider: no cloud dependency, free).
    **No Anthropic embeddings adapter — Anthropic has no embeddings endpoint.** Adapter
    config-only (`ModelClient:EmbeddingProvider`).
- **Storage** — two config-selected ports: **`IVectorStore`** for chunks (Azure AI Search Free tier
  live / in-memory cosine offline — ADR-0017) and **`IDocumentStore`** for the original file + metadata
  (Azure Blob Storage live / in-memory offline — ADR-0018). Swap via `VectorStore:Provider` /
  `DocumentStore:Provider`, never a code change.
- **Usage telemetry** — a third config-selected port, **`IUsageSource`** (read-only, not persistence):
  LLM token/cost/latency metrics for the dashboard (Azure Monitor platform metrics live / in-memory
  offline — ADR-0020). Swap via `UsageSource:Provider`; cost is **computed** from a non-secret price
  table by a pure calculator outside the adapter.
- **Frontend** — React + TypeScript SPA with four tabs: **Workspace** (drag-drop upload →
  extracted-field tiles · question box → answer-with-citations · a source-file viewer), **Documents**
  (the full-width persisted documents table — search / CSV export / multi-select delete / row "View";
  fields shown as a count), **Dashboard** (KPI tiles, charts, needs-review, and lease expirations,
  aggregated from `GET /documents`), and **Ops / Usage** (operator-facing LLM usage: totals ·
  per-deployment table · request-health + latency cards, over `GET /usage` — ADR-0020). The source-file
  viewer is a modal showing the extracted fields **beside** the original file.
- **RAG pipeline** — ingest PDF/text/Markdown → extract structured fields → chunk → embed
  (`IEmbeddingClient`) → store chunks (`IVectorStore`) + persist the original file/metadata
  (`IDocumentStore`) → on ask, retrieve top-k by cosine → answer **with citations**. Stores are
  config-selected: Azure AI Search + Azure Blob live; in-memory offline/test.

### Models & cost
Default chat model `claude-opus-4-8` (adaptive thinking). Sonnet 4.6 or Haiku 4.5 are selectable
per call-type for cost — e.g. the extraction step. Lean on **prompt caching** for the repeated
document context. All model IDs live in config, never hardcoded. Token usage + **estimated** cost are
surfaced in-app via the **Ops / Usage** dashboard, read from free Azure Monitor platform metrics (ADR-0020).

### Out of scope — "production hardening", do NOT build
VNet/Private Link · Azure AI Document Intelligence OCR tuning · Azure AI Search beyond the Free-tier
vector store (semantic ranker / reranking, Basic+ scale — ADR-0017 brought the Free tier in as the
live store) · auth/RBAC · the **infra** observability stack — Log Analytics ingestion, Application
Insights traces, alerting (the **in-app** LLM usage dashboard reading free, read-only Azure Monitor
platform metrics is **in** scope — ADR-0020; it's a product surface, not an infra stack). If a task
seems to need an out-of-scope item, stub it and note why in `knowledge/lessons.md`.

## Coding conventions
**C#** — nullable reference types **enabled**; `async`/`await` end-to-end (never `.Result` /
`.Wait()`); constructor injection via the built-in DI container; **file-scoped namespaces**; one
public type per file; validate and throw early on bad input.

**TypeScript** — `strict: true`; **function components + hooks** (no class components); a single
**typed API client** wraps `fetch` (no ad-hoc `fetch` in components); explicit return types on
exported functions; no `any` — use `unknown` and narrow.

## Build / test / run
Code changes follow the **`tdd` skill** (`.claude/skills/tdd/`): new behavior ships with tests, the
suite stays green (`dotnet test` / `npm test`), the governing spec is known (it offers `/spec` if none
— never blocks), and relevant lessons/ARCHITECTURE/ADRs are honored first. It auto-engages on any
`/backend` or `/frontend` code work — no need to invoke it.

**Backend** (`/backend`)
- `dotnet build`
- `dotnet test`
- `dotnet run --project src/LandDoc.Api`

**Frontend** (`/frontend`)
- `npm install`
- `npm run dev`
- `npm test`

## Guardrails — what NOT to touch
- **Secrets** — never commit them. Dev: `dotnet user-secrets` / environment variables. Prod:
  Azure Key Vault. No keys, connection strings, or tokens in source, `appsettings.*`, or history.
- **Public interfaces / ports** — do not change any interface that adapters or callers depend on
  (the model-access ports today; any future seam) without a written spec in `knowledge/docs/specs/`. Every
  implementation and caller depends on the contract, so such a change is an architecture decision,
  not a quick edit.
- **Generated / build output** — never hand-edit `bin/`, `obj/`, `dist/`, or generated clients.
  Change the source and regenerate.
- **Scope** — keep the out-of-scope items out. Don't add infrastructure we said we wouldn't
  build; stub it and note it instead.

## Project docs — where things live, and what each holds
Docs are authored as **design intent** before code and kept current by `/wrap`. **To answer a
question about the system, read the relevant file below before guessing.** Where a doc is silent or
unsettled, surface the gap and ask — don't invent answers.

```
README.md          what this is + repo map (browse-first entry point)
CONTRIBUTING.md    how to work here + the doc workflow
CLAUDE.md          this file — architecture · conventions · guardrails
.github/           CI / PR templates (reserved)
knowledge/README.md     knowledge index / TOC
knowledge/docs/
  PRD.md           problem · goals · non-goals · users · scope · success metrics · open questions
  STACK.md         layer · choice · version · why
  ARCHITECTURE.md  system + component diagrams · ports/adapters · cross-cutting concerns · conventions
  DATA-MODEL.md    domain types · ER diagram · invariants
  DATA-FLOW.md     ingest · ask · delete · usage sequence diagrams
  API.md           endpoints · request/response shapes · error model
  RUNBOOK.md       install · run · test · build · env/secret names · teardown
  DEPLOYMENT.md    first-time deploy · redeploy · teardown for Azure Container Apps
  CICD.md          OIDC identity setup · role grants · GitHub secrets · CI/CD usage
  AZURE-CONFIG.md  Azure resource inventory · adapter wiring · model deployments · role grants
  USAGE-DASHBOARD.md  LLM usage/cost Ops dashboard — how it works · config keys · local + Azure setup
  GLOSSARY.md      domain + project terms
  decisions/       ADRs (Nygard, NNNN-slug.md), immutable once Accepted — supersede convention below
  specs/           feature specs, one per file (NNNN-<slug>.md); design interface changes here
knowledge/notes/        evergreen knowledge, one topic per file, [[wikilinks]], accrued by /wrap
knowledge/logs/         committed session logs YYYY-MM-DD.md, appended by /wrap
knowledge/lessons.md    lessons log "[date] | what happened/learned | rule or takeaway"
```

ADRs are immutable once **Accepted** — a changed decision is a *new* ADR that supersedes the old one
(old one's Status → `Superseded by NNNN`, cross-linked both ways; the file is **never renamed or
deleted**). To find the current call on a topic, read an ADR's Status and follow the pointer — don't
trust recency or a number cited elsewhere.

Maintained by the commands: `/kb-init` scaffolds · `/spec` opens a spec · `/issues` turns an accepted
spec into dependency-ordered GitHub issues · `/adr` records a decision · `/wrap` logs the session and
**flags** doc drift · `/reconcile` closes that drift (you pick the direction, per item).
