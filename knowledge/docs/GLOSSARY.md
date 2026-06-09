# Glossary

The ubiquitous language for LandDoc Intelligence — domain terms and project/architecture terms.

## Domain (land & title)
- **Lease** — agreement granting the right to explore/produce minerals from a tract.
- **Lessor / Lessee** — party granting the lease (owner) / party receiving it (operator).
- **Royalty** — the lessor's share of production revenue, free of production cost (e.g. 3/16).
- **Title opinion** — an attorney's analysis of who owns what interests in a tract.
- **Mineral rights** — ownership of the minerals beneath a surface tract (severable from surface).
- **Legal description** — the formal locator for a parcel (e.g. section/township/range, metes/bounds).
- **County records** — recorded instruments (deeds, leases, assignments) filed at the county level.
- **Grantor / Grantee** — party conveying an interest / party receiving it.
- **Encumbrance** — a claim or liability against title (lien, mortgage, easement).

## Project & architecture
- **RAG** — retrieval-augmented generation: ground answers in retrieved source text.
- **Ingestion / Extraction / Retrieval / Qa / Usage** — the five backend modules (PDF→store / fields /
  top-k / cited answer / LLM usage telemetry).
- **Chunk** — a contiguous slice of document text that gets embedded and retrieved.
- **Embedding** — a `float[]` vector representation of a chunk or query.
- **Vector store** — collection of chunks+vectors; Azure AI Search Free tier is the live store, in-memory cosine the offline/test provider; see [ADR-0017](decisions/0017-azure-ai-search-free-tier-live-vector-store.md) (realizes [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md)).
- **Document store** — persists each document's original file + metadata + extracted fields, separate from the chunk index; Azure Blob Storage live, in-memory offline/test (`IDocumentStore`); see [ADR-0018](decisions/0018-persisted-document-store-azure-blob-for-original-files-and-metadata.md).
- **Cosine similarity** — the metric used to rank chunks against a query vector.
- **Citation** — a pointer from an answer/field back to the source chunk that supports it; carries the source document's file name so the UI can link to it.
- **Dashboard** — the read-only analytics view over the ingested corpus (KPI tiles, documents-by-location and ingest-over-time charts, a needs-review list, and lease expirations), aggregated client-side from `GET /documents`; see [spec 0007](specs/0007-insights-dashboard-and-document-search-export.md).
- **Lease expiration** — a document's term/expiration (end) date; surfaced (when extracted) in the dashboard's expirations widget, bucketed by how soon it's due.
- **Ops / Usage** — the operator-facing dashboard (distinct from the analyst Dashboard) showing LLM token usage, estimated cost, request health, and latency over `GET /usage`; see [spec 0009](specs/0009-llm-usage-and-cost-ops-dashboard.md).
- **Usage source** — the read-only `IUsageSource` port behind the usage dashboard: Azure Monitor platform metrics live, in-memory offline/test, config-selected via `UsageSource:Provider`; cost is computed from a price table, not measured; see [ADR-0020](decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md).
- **Azure Monitor (platform metrics)** — the free, ~93-day, 1-minute-grain metrics every Azure resource emits automatically; the usage dashboard reads the Foundry resource's token/request/latency metrics via the `Azure.Monitor.Query` SDK (Monitoring Reader role, managed identity) — ADR-0020.
- **`IChatClient` / `IEmbeddingClient`** — the two model-access ports.
- **Adapter / Port** — hexagonal terms: port = interface, adapter = provider implementation.
- **Foundry** — Microsoft Foundry / Azure AI Services, the Azure model gateway. Hosts the live Azure OpenAI GPT chat deployment and the live embedding model (`text-embedding-3-small`, see [ADR-0013](decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md)); the Foundry-serves-Claude chat primary was retired (needs Enterprise/MCA-E) — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md).
- **Azure OpenAI** — directly-sold Azure model service (PAYG-eligible); serves the live slice chat model (`gpt-5.4-mini`, OpenAI Chat Completions) via `AzureOpenAIChatClient` — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md).
- **Modular monolith** — one deployable process, modules separated by namespace; chosen over microservices, see [ADR-0004](decisions/0004-modular-monolith-over-microservices.md).
- **Vertical slice** — a thin end-to-end implementation proving the whole flow, not production-grade.
