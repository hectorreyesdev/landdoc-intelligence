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
- [Runbook (index)](docs/RUNBOOK.md) — entry point + canonical config/secrets reference
- [Runbook — local](docs/RUNBOOK-LOCAL.md) — run & debug on your machine (dev · container · tests)
- [Runbook — production](docs/RUNBOOK-PROD.md) — operate the live env (deploy · logs · rollback · scale)
- [Deployment](docs/DEPLOYMENT.md) — Azure Container Apps: first-time deploy · redeploy · custom domain · teardown
- [CI/CD](docs/CICD.md) — auto-deploy on merge to main (GitHub Actions + OIDC)
- [Eval harness](docs/EVAL-HARNESS.md) — RAG answer-quality eval: run · Azure/Foundry touchpoints · one-time setup · troubleshooting · from-scratch
- [Azure config](docs/AZURE-CONFIG.md) — Azure resource inventory · adapter wiring · role grants
- [Usage dashboard](docs/USAGE-DASHBOARD.md) — LLM usage/cost Ops dashboard: how it works · keys · local + Azure config
- [Glossary](docs/GLOSSARY.md) — domain + project terms
- [Decisions](docs/decisions/) — ADRs ([0001](docs/decisions/0001-record-architecture-decisions.md) · [0002](docs/decisions/0002-split-model-access-into-chat-and-embedding-clients.md) · [0003](docs/decisions/0003-dotnet-10-lts.md) · [0004](docs/decisions/0004-modular-monolith-over-microservices.md) · [0005](docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) · [0006](docs/decisions/0006-react-typescript-frontend-over-blazor.md) · [0007](docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md) · [0008](docs/decisions/0008-deterministic-hashing-embeddings-for-slice.md) · [0009](docs/decisions/0009-corpus-wide-ask-retrieval-scope.md) · [0010](docs/decisions/0010-anthropic-direct-slice-default-chat-adapter.md) · [0011](docs/decisions/0011-single-origin-spa-api-topology.md) · [0012](docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md) · [0013](docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md) · [0014](docs/decisions/0014-surface-source-document-identity-in-ask-grounding-context.md) · [0015](docs/decisions/0015-field-extraction-generic-role-neutral-schema-land-document-types.md) · [0016](docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets.md) · [0017](docs/decisions/0017-azure-ai-search-free-tier-live-vector-store.md) · [0018](docs/decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata.md) · [0019](docs/decisions/0019-hard-best-effort-non-transactional-document-deletion.md) · [0020](docs/decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md) · [0021](docs/decisions/0021-llm-eval-harness-and-judge-model.md))
- [Specs](docs/specs/README.md) — feature specs, one per file (`NNNN-<slug>.md`)

## Knowledge & journal
- [notes/](notes/README.md) — evergreen project knowledge (`[[wikilinks]]`), accrued by `/wrap`
- [logs/](logs/README.md) — **committed** session logs (`YYYY-MM-DD.md`), appended by `/wrap`
- [lessons.md](lessons.md) — lessons learned (`[date] | what happened/learned | rule`), appended by `/wrap`
