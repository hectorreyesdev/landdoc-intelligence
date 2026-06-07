# Contributing

How to work in this repo. The workflow is part of the deliverable — the agentic process is meant to
be visible, which is why the session journal under `knowledge/logs/` is committed.

## Doc-first workflow
Docs are authored **before** code, as the design the scaffold will implement:
- `/kb-init` — scaffold the doc tree (idempotent; seeds derivable facts, leaves judgment as `AUTHOR` markers).
- `/spec` — open a feature spec under [`knowledge/docs/specs/`](knowledge/docs/specs/README.md) (`NNNN-<slug>.md`).
- `/adr` — record an architecture decision in [`knowledge/docs/decisions/`](knowledge/docs/decisions/) (Nygard format).
- `/wrap` — after code lands, reconcile docs with reality and append a session log to `knowledge/logs/`.

## Code layout
- `backend/` — ASP.NET Core (.NET 10) modular monolith (`Ingestion` · `Extraction` · `Retrieval` · `Qa`).
- `frontend/` — React + TypeScript SPA.
- Conventions live in [`CLAUDE.md`](CLAUDE.md); architecture rationale in [`knowledge/docs/ARCHITECTURE.md`](knowledge/docs/ARCHITECTURE.md).

## Commits
- No `Co-Authored-By` / "Generated with" trailers — keep messages clean.
- Never commit secrets (dev: `dotnet user-secrets` / env vars; prod: Azure Key Vault).

## Branches & PRs
- Work on **feature branches**; open a **PR into `main`** and **squash-merge** — reviewable history
  even when solo.
- The PR is where review runs (`/code-review`) and, once it exists, CI — vet before merge.
- Keep one logical change per PR; reference the spec/ADR it implements.

## Definition of done
A change is **done** when all of these hold:
- **Tests green + clean build** — `dotnet test` / `npm test` pass (the `tdd` skill's bar).
- **Spec exists** — a feature/non-trivial change has a committed `knowledge/docs/specs/NNNN-*.md` *before*
  implementation (spec-first).
- **ADR for architectural calls** — any architecture decision is recorded (or superseded) under
  `knowledge/docs/decisions/`.
- **Docs reconciled** — doc drift is closed and a session log appended via `/wrap`.
