# Contributing

How to work in this repo. The workflow is part of the deliverable — the agentic process is meant to
be visible, which is why the session journal under `wiki/logs/` is committed.

## Doc-first workflow
Docs are authored **before** code, as the design the scaffold will implement:
- `/wiki-init` — scaffold the doc tree (idempotent; seeds derivable facts, leaves judgment as `AUTHOR` markers).
- `/spec` — open a feature spec under [`specs/`](specs/README.md) (`NNNN-<slug>.md`).
- `/adr` — record an architecture decision in [`wiki/docs/decisions/`](wiki/docs/decisions/) (Nygard format).
- `/wrap` — after code lands, reconcile docs with reality and append a session log to `wiki/logs/`.

## Code layout
- `backend/` — ASP.NET Core (.NET 10) modular monolith (`Ingestion` · `Extraction` · `Retrieval` · `Qa`).
- `frontend/` — React + TypeScript SPA.
- Conventions live in [`CLAUDE.md`](CLAUDE.md); architecture rationale in [`wiki/docs/ARCHITECTURE.md`](wiki/docs/ARCHITECTURE.md).

## Commits
- No `Co-Authored-By` / "Generated with" trailers — keep messages clean.
- Never commit secrets (dev: `dotnet user-secrets` / env vars; prod: Azure Key Vault).

> [!note] AUTHOR: branch & PR conventions, and the definition of done for this repo.
