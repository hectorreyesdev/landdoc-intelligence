# LandDoc Intelligence

AI document-intelligence + retrieval-augmented Q&A over land/title documents (leases, title
opinions, county records). **Vertical slice, NOT production** — build the simplest thing that
proves the end-to-end flow. Read `tasks/lessons.md` at the start of every session (see bottom).

## Architecture
- **Backend** — ASP.NET Core Web API on **.NET 10 (LTS)**. Modular monolith: one process,
  modules split by folder/namespace (`Ingestion`, `Extraction`, `Retrieval`, `Qa`) — not
  microservices.
- **Model access** — split into TWO interfaces (chat and embeddings have different providers and
  fail over differently):
  - **`IChatClient`** — chat/completions. `FoundryChatClient` (Microsoft Foundry gateway,
    **primary**; may serve a Claude *or* a GPT model) + `AnthropicChatClient` (Anthropic API
    direct, **fallback**, `Anthropic` NuGet SDK). Adapter is **config-only**
    (`ModelClient:ChatProvider`), never a code change.
  - **`IEmbeddingClient`** — embeddings only. `LocalEmbeddingClient` (local in-memory model — the
    **slice default**: no cloud dependency, self-contained, free) + `FoundryEmbeddingClient`
    (Azure OpenAI `text-embedding-3-small` via the Foundry gateway, the **production path**).
    **No Anthropic embeddings adapter — Anthropic has no embeddings endpoint.** Adapter
    config-only (`ModelClient:EmbeddingProvider`).
- **Frontend** — React + TypeScript SPA: upload control → extracted-fields view → question box
  → answer-with-citations.
- **RAG pipeline** — ingest PDF → extract structured fields → chunk → embed (`IEmbeddingClient`)
  → **in-memory cosine similarity over `float[]`** → retrieve top-k → answer **with citations**.
  Vector store is in-memory for the slice; **Azure AI Search** is the production path (out of
  scope to build).

### Models & cost
Default chat model `claude-opus-4-8` (adaptive thinking). Sonnet 4.6 or Haiku 4.5 are selectable
per call-type for cost — e.g. the extraction step. Lean on **prompt caching** for the repeated
document context. All model IDs live in config, never hardcoded.

### Out of scope — "production hardening", do NOT build
VNet/Private Link · Azure AI Document Intelligence OCR tuning · Azure AI Search · auth/RBAC ·
observability stack. If a task seems to need one, stub it and note why in `tasks/lessons.md`.

## Coding conventions
**C#** — nullable reference types **enabled**; `async`/`await` end-to-end (never `.Result` /
`.Wait()`); constructor injection via the built-in DI container; **file-scoped namespaces**; one
public type per file; `record` types for DTOs; validate and throw early on bad input.

**TypeScript** — `strict: true`; **function components + hooks** (no class components); a single
**typed API client** wraps `fetch` (no ad-hoc `fetch` in components); explicit return types on
exported functions; no `any` — use `unknown` and narrow.

## Build / test / run
**Backend** (`/backend`)
- `dotnet build`
- `dotnet test`
- `dotnet run --project src/LandDoc.Api`

**Frontend** (`/frontend`)
- `npm install`
- `npm run dev`
- `npm test`

> TODO: solution and projects are not scaffolded yet. Create `/backend` and `/frontend`, then
> make these commands real and delete this note.

## Guardrails — what NOT to touch
- **Secrets** — never commit them. Dev: `dotnet user-secrets` / environment variables. Prod:
  Azure Key Vault. No keys, connection strings, or tokens in source, `appsettings.*`, or history.
- **`IChatClient` / `IEmbeddingClient`** — do not change either interface without a written spec
  in `/specs`. Adapters and every caller depend on them; an interface change is an architecture
  decision, not a quick edit.
- **Generated / build output** — never hand-edit `bin/`, `obj/`, `dist/`, or generated clients.
  Change the source and regenerate.
- **Scope** — keep the out-of-scope items out. Don't add infrastructure we said we wouldn't
  build; stub it and note it instead.

## Every session
Read `tasks/lessons.md` first — it records what broke before and the rule that prevents a repeat.
Append a line whenever you learn something the hard way.
