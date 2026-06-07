# LandDoc Intelligence

An ASP.NET Core (.NET 10) Web API + React/TypeScript SPA running a retrieval-augmented Q&A
**vertical slice** over land/title documents: ingest a PDF → extract structured fields → chunk →
embed → in-memory cosine retrieval → answer **with citations**. Model access flows through two
config-swappable ports — `IChatClient` (Microsoft Foundry primary, Anthropic fallback) and
`IEmbeddingClient` (local in-memory slice default, Azure OpenAI production) — so providers change by
configuration, not code.

Senior-level judgment made visible: deliberate scope (build vs. stub), a spec- and ADR-first
workflow, and an agentic process that's part of the deliverable.

## Repo map
- [`CLAUDE.md`](CLAUDE.md) — project constitution (architecture · conventions · guardrails)
- [`CONTRIBUTING.md`](CONTRIBUTING.md) — how to work in this repo + the doc workflow
- [`specs/`](specs/README.md) — feature specs (one file per feature)
- [`tasks/lessons.md`](tasks/lessons.md) — running lessons-learned log
- [`wiki/`](wiki/README.md) — living docs, evergreen notes, committed session logs
  - [PRD](wiki/docs/PRD.md) · [Stack](wiki/docs/STACK.md) · [Architecture](wiki/docs/ARCHITECTURE.md) · [Data model](wiki/docs/DATA-MODEL.md) · [Data flow](wiki/docs/DATA-FLOW.md) · [API](wiki/docs/API.md) · [Runbook](wiki/docs/RUNBOOK.md) · [Glossary](wiki/docs/GLOSSARY.md) · [Decisions](wiki/docs/decisions/)
- `backend/` · `frontend/` — application code (not yet scaffolded)

> Docs are authored as **design intent** before code; once code lands, `/wrap` keeps them current.
