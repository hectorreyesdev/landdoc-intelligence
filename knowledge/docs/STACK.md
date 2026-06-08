# Stack

Seeded from `CLAUDE.md`. Backend and frontend rows are pinned from their manifests (`backend/` · `frontend/package.json`).

| Layer | Choice | Version | Why |
|---|---|---|---|
| Backend runtime | .NET | 10 (LTS) | LTS through 2028-11-14 (vs. .NET 8/9 EOL 2026-11-10); modern C#; first-class DI + hosting — see [ADR-0003](decisions/0003-dotnet-10-lts.md) |
| Backend framework | ASP.NET Core Web API | 10 | HTTP surface of the modular monolith |
| Language (backend) | C# | 14 (.NET 10) | Nullable enabled, records, async/await |
| Chat models | Azure OpenAI GPT (live) · Anthropic Claude (fallback) | `gpt-5.4-mini` live · `claude-opus-4-8` fallback | Live slice chat = Azure OpenAI GPT (OpenAI Chat Completions); Anthropic-direct is the config-swap fallback — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md) |
| Azure OpenAI SDK | `Azure.AI.OpenAI` (NuGet) | 2.1.0 | `AzureOpenAIChatClient` — live chat via OpenAI Chat Completions; per-provider config — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md) |
| Anthropic SDK | `Anthropic` (NuGet) | 12.27.0 | `AnthropicChatClient` — config-swap **fallback** chat adapter — see [ADR-0012](decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md) |
| Embeddings (live slice) | Azure OpenAI `text-embedding-3-small` (`AzureOpenAIEmbeddingClient`, `Azure.AI.OpenAI`) | model via Azure · SDK 2.1.0 | Live slice default — semantic retrieval that ranks by meaning; config-selected via `ModelClient:EmbeddingProvider` — see [ADR-0013](decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md) |
| Embeddings (offline/test) | `LocalEmbeddingClient` — deterministic hashing / bag-of-words | n/a (in-repo) | Offline/test fallback — free, deterministic, no network; pin `EmbeddingProvider=local` — see [ADR-0013](decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md) (supersedes [ADR-0008](decisions/0008-deterministic-hashing-embeddings-for-slice.md)) |
| Vector store (live) | Azure AI Search Free tier (`AzureAiSearchVectorStore`, `Azure.Search.Documents`) over `landdoc-chunks` (256-d HNSW + cosine) | tier: Free | Decided live default — persistence across restarts at $0; config-selected via `VectorStore:Provider=azuresearch` — see [ADR-0017](decisions/0017-azure-ai-search-free-tier-live-vector-store.md) (realizes [ADR-0005](decisions/0005-in-memory-vector-store-slice-azure-ai-search-production.md)) |
| Vector store (offline/test) | In-memory cosine over `float[]` (`InMemoryVectorStore`) | n/a (in-repo) | Offline/test provider — pin `VectorStore:Provider=inmemory`; no creds, no network — see [ADR-0017](decisions/0017-azure-ai-search-free-tier-live-vector-store.md) |
| Frontend | React | 19 (`^19.2`) | SPA: upload, fields, ask, cited answer — React over Blazor, see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Language (frontend) | TypeScript | 6 (`^6.0`, `strict`) | Type-safe UI + typed API client — see [ADR-0006](decisions/0006-react-typescript-frontend-over-blazor.md) |
| Build (frontend) | Vite (+ `@vitejs/plugin-react`) | `^8.0` · `^6.0` | Dev server + bundling; single-origin dev proxy — see [ADR-0011](decisions/0011-single-origin-spa-api-topology.md) |
| PDF text extraction | `PdfPig` (UglyToad, NuGet) | 0.1.14 | PDF → text for chunking (text-based; no OCR) |
| Test (backend) | xUnit · `Microsoft.NET.Test.Sdk` · `Microsoft.AspNetCore.Mvc.Testing` | 2.9.3 · 17.14.1 · 10.0.8 | `dotnet test`; `WebApplicationFactory` integration tests |
| Test (frontend) | Vitest (+ React Testing Library · jsdom) | `^4.1` · `^16.3` · `^29.1` | `npm test`; component + typed-client tests |
| Secrets (dev) | `dotnet user-secrets` / env vars | n/a | Never commit secrets |
| Secrets (prod) | Azure Key Vault via `DefaultAzureCredential` (managed identity in ACA) | `Azure.Identity` 1.21.0 · `Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.1 | **Built** — opt-in config source (`KeyVault:Uri`); vault secrets overlay config keys, no adapter change — see [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md) |
| Container image | Docker — multi-stage (`node:22-alpine` build SPA + `dotnet/sdk:10.0` publish API → `dotnet/aspnet:10.0`) | n/a | One image serves the SPA (`wwwroot`) and API on one origin, port 8080 — see [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md) |
| Hosting (prod) | Azure Container Apps | n/a | Single container, public HTTPS ingress; secrets via managed identity + Key Vault — see [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md) · [DEPLOYMENT.md](DEPLOYMENT.md) |
| CI/CD | GitHub Actions → `az acr build` → ACA revision (passwordless OIDC) | n/a | Auto-deploy on merge to `main` — see [CICD.md](CICD.md) |
