# Stack

Seeded from `CLAUDE.md`. Rows marked `TODO` are not pinned yet — greenfield, no manifests exist.

| Layer | Choice | Version | Why |
|---|---|---|---|
| Backend runtime | .NET | 10 (LTS) | LTS through 2028-11-14 (vs. .NET 8/9 EOL 2026-11-10); modern C#; first-class DI + hosting — see [ADR-0003](decisions/0003-dotnet-10-lts.md) |
| Backend framework | ASP.NET Core Web API | 10 | HTTP surface of the modular monolith |
| Language (backend) | C# | 14 (.NET 10) | Nullable enabled, records, async/await |
| Chat models | Claude (via Foundry / Anthropic) | `claude-opus-4-8` default | Foundry primary (Claude **or** GPT); Anthropic direct fallback — see [ADR-0007](decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md) |
| Anthropic SDK | `Anthropic` (NuGet) | TODO: pin | Direct-to-Anthropic fallback adapter — see [ADR-0007](decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback.md) |
| Embeddings (slice) | Local in-memory model | TODO: choose | Self-contained, no cloud dependency, free |
| Embeddings (prod) | Azure OpenAI `text-embedding-3-small` via Foundry | n/a (not built) | Production path only |
| Vector store (slice) | In-memory cosine over `float[]` | n/a | Simplest thing that proves retrieval — see [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) |
| Vector store (prod) | Azure AI Search | n/a (out of scope) | Production path only — see [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md) |
| Frontend | React | TODO: pin | SPA: upload, fields, ask, cited answer — React over Blazor, see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Language (frontend) | TypeScript | TODO: pin (`strict`) | Type-safe UI + typed API client — see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Build (frontend) | TODO: choose (Vite likely) | TODO | Dev server + bundling |
| PDF text extraction | TODO: choose (e.g. PdfPig) | TODO | PDF → text for chunking |
| Test (backend) | TODO: choose (xUnit likely) | TODO | `dotnet test` |
| Test (frontend) | TODO: choose (Vitest likely) | TODO | `npm test` |
| Secrets (dev) | `dotnet user-secrets` / env vars | n/a | Never commit secrets |
| Secrets (prod) | Azure Key Vault | n/a (out of scope) | Production path only |

> TODO: replace the `TODO` rows once `/backend` and `/frontend` are scaffolded and manifests exist.
