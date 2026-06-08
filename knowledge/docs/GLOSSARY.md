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
- **Ingestion / Extraction / Retrieval / Qa** — the four backend modules (PDF→store / fields /
  top-k / cited answer).
- **Chunk** — a contiguous slice of document text that gets embedded and retrieved.
- **Embedding** — a `float[]` vector representation of a chunk or query.
- **Vector store** — collection of chunks+vectors; in-memory cosine for the slice, Azure AI Search in prod; see [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md).
- **Cosine similarity** — the metric used to rank chunks against a query vector.
- **Citation** — a pointer from an answer/field back to the source chunk that supports it.
- **`IChatClient` / `IEmbeddingClient`** — the two model-access ports.
- **Adapter / Port** — hexagonal terms: port = interface, adapter = provider implementation.
- **Foundry** — Microsoft Foundry / Azure AI Services, the Azure model gateway. Hosts the live Azure OpenAI GPT chat deployment and the live embedding model (`text-embedding-3-small`, see [ADR-0013](decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md)); the Foundry-serves-Claude chat primary was retired (needs Enterprise/MCA-E) — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md).
- **Azure OpenAI** — directly-sold Azure model service (PAYG-eligible); serves the live slice chat model (`gpt-5.4-mini`, OpenAI Chat Completions) via `AzureOpenAIChatClient` — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md).
- **Modular monolith** — one deployable process, modules separated by namespace; chosen over microservices, see [ADR-0004](decisions/0004-modular-monolith-over-microservices.md).
- **Vertical slice** — a thin end-to-end implementation proving the whole flow, not production-grade.
