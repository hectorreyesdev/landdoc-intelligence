# LandDoc Intelligence — Knowledge Base

Living documentation for the slice (see the [repo README](../README.md) for what this is). Docs are
authored as **design intent** first, then become living docs that `/wrap` drift-checks once code
lands. Conventions live in [`CLAUDE.md`](../CLAUDE.md) and the
[Architecture](docs/ARCHITECTURE.md) cross-cutting section — there is no separate PATTERNS doc.

## Docs (`docs/`)
- [PRD](docs/PRD.md) — problem · goals · scope · success metrics
- [Stack](docs/STACK.md) — choices · versions · rationale
- [Architecture](docs/ARCHITECTURE.md) — context · components · ports/adapters · cross-cutting
- [Data model](docs/DATA-MODEL.md) — domain types + ER diagram
- [Data flow](docs/DATA-FLOW.md) — ingest → answer-with-citations sequence
- [API](docs/API.md) — endpoints · request/response · error model
- [Runbook](docs/RUNBOOK.md) — install · run · test · build · secrets · teardown
- [Glossary](docs/GLOSSARY.md) — domain + project terms
- [Decisions](docs/decisions/) — ADRs ([0001](docs/decisions/0001-record-architecture-decisions.md) · [0002](docs/decisions/0002-split-model-access-into-chat-and-embedding-clients.md) · [0003](docs/decisions/0003-dotnet-10-lts.md) · [0004](docs/decisions/0004-modular-monolith-over-microservices.md) · [0005](docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) · [0006](docs/decisions/0006-react-typescript-frontend-over-blazor.md) · [0007](docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md))
- [Specs](docs/specs/README.md) — feature specs, one per file (`NNNN-<slug>.md`)

## Knowledge & journal
- [notes/](notes/README.md) — evergreen project knowledge (`[[wikilinks]]`), accrued by `/wrap`
- [logs/](logs/README.md) — **committed** session logs (`YYYY-MM-DD.md`), appended by `/wrap`
- [lessons.md](lessons.md) — lessons learned (`[date] | what happened/learned | rule`), appended by `/wrap`
