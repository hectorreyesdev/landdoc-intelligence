# Stack

Seeded from `CLAUDE.md`. Backend and frontend rows are pinned from their manifests (`backend/` · `frontend/package.json`).

| Layer | Choice | Version | Why |
|---|---|---|---|
| Backend runtime | .NET | 10 (LTS) | LTS through 2028-11-14 (vs. .NET 8/9 EOL 2026-11-10); modern C#; first-class DI + hosting — see [ADR-0003](decisions/0003-dotnet-10-lts.md) |
| Backend framework | ASP.NET Core Web API | 10 | HTTP surface of the modular monolith |
| Language (backend) | C# | 14 (.NET 10) | Nullable enabled, records, async/await |
| Chat models | Claude (via Foundry / Anthropic) | `claude-opus-4-8` default | Foundry primary (Claude **or** GPT); Anthropic direct fallback — see [ADR-0007](decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md) |
| Anthropic SDK | `Anthropic` (NuGet) | TODO: pin | Direct-to-Anthropic fallback adapter — see [ADR-0007](decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md) |
| Embeddings (slice) | `LocalEmbeddingClient` — deterministic hashing / bag-of-words | n/a (in-repo) | Self-contained, free, deterministic tests — see [ADR-0008](decisions/0008-deterministic-hashing-embeddings-for-slice.md) |
| Embeddings (prod) | Azure OpenAI `text-embedding-3-small` via Foundry | n/a (not built) | Production path only |
| Vector store (slice) | In-memory cosine over `float[]` | n/a | Simplest thing that proves retrieval — see [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) |
| Vector store (prod) | Azure AI Search | n/a (out of scope) | Production path only — see [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) |
| Frontend | React | 19 (`^19.2`) | SPA: upload, fields, ask, cited answer — React over Blazor, see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Language (frontend) | TypeScript | 6 (`^6.0`, `strict`) | Type-safe UI + typed API client — see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Build (frontend) | Vite (+ `@vitejs/plugin-react`) | `^8.0` · `^6.0` | Dev server + bundling; single-origin dev proxy — see [ADR-0011](decisions/0011-single-origin-spa-api-topology.md) |
| PDF text extraction | `UglyToad.PdfPig` (NuGet) | 1.7.0-custom-5 | PDF → text for chunking (text-based; no OCR). Only prerelease-tagged builds are published |
| Test (backend) | xUnit · `Microsoft.NET.Test.Sdk` · `Microsoft.AspNetCore.Mvc.Testing` | 2.9.3 · 17.14.1 · 10.0.8 | `dotnet test`; `WebApplicationFactory` integration tests |
| Test (frontend) | Vitest (+ React Testing Library · jsdom) | `^4.1` · `^16.3` · `^29.1` | `npm test`; component + typed-client tests |
| Secrets (dev) | `dotnet user-secrets` / env vars | n/a | Never commit secrets |
| Secrets (prod) | Azure Key Vault | n/a (out of scope) | Production path only |
