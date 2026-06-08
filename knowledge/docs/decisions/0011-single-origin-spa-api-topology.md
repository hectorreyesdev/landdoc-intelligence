# 0011. Single-origin SPA ↔ API topology (Vite dev-proxy now, SWA linked-backend in prod — no CORS)

- Status: Superseded by [ADR-0016](0016-single-container-azure-container-apps-keyvault-secrets.md)
  — the single-origin / no-CORS principle and the relative-paths client contract **carry forward**; only
  the prod *realization* named here (Azure Static Web Apps + linked backend) is replaced by the single
  container on Azure Container Apps.
- Date: 2026-06-07

## Context
The slice is gaining its frontend ([[knowledge/docs/specs/0003-frontend-vertical-slice]] — React + TS
SPA, Accepted). [[knowledge/docs/decisions/0006-react-typescript-frontend-over-blazor]] settled the
**framework** (React over Blazor) and named the boundary as "HTTP/JSON through one typed client," but
it is **silent on transport** — how the SPA actually reaches the ASP.NET Core API across origins. Spec
0003 pinned the *dev* half (Vite dev-proxy, same-origin relative paths) and deliberately put CORS and
an absolute API base URL **out of scope**, leaving the topology itself unrecorded. This ADR is that
record.

Forces at play:
- A browser SPA and an API are, by default, **two origins** (different ports in dev; potentially
  different hosts in prod). Cross-origin browser calls drag in **CORS** — preflight requests, an
  origin allow-list to maintain, credential/cookie rules — configuration that is easy to get subtly
  wrong and a recurring source of "works locally, blocked in prod" failures.
- The boundary is already **HTTP/JSON through a single typed client** (ADR-0006; ARCHITECTURE "Key
  boundaries"), so where that client's base path resolves is a free choice — relative, same-origin
  paths work as long as something serves the SPA and the API under one origin.
- **Dev:** the Vite dev server already fronts the SPA; its dev-proxy can forward `/documents` and
  `/ask` to the API process, so the browser only ever talks to Vite's origin.
- **Prod (named, not built):** Azure **Static Web Apps** serves the static SPA and supports a **linked
  backend** (Bring-Your-Own-Backend) — SWA reverse-proxies the API routes to the linked Azure backend
  **under the SWA's own origin**, so the browser again sees a single origin.
- Repo posture: production infrastructure is consistently **named but not built** in the slice
  (in-memory store → Azure AI Search, [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]];
  Foundry failover, [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]]).
  Recording a prod topology we don't build fits that pattern; building it does not — `CLAUDE.md` keeps
  "production hardening" out of scope.

Relates to [[knowledge/docs/specs/0003-frontend-vertical-slice]]; builds on
[[knowledge/docs/decisions/0006-react-typescript-frontend-over-blazor]] (framework) and the boundary
in [[knowledge/docs/decisions/0004-modular-monolith-over-microservices]].

## Decision
We will treat the SPA and API as **a single origin in every environment**, so that **CORS never exists
in this architecture**. In **development**, the **Vite dev-proxy** forwards the API routes
(`/documents`, `/ask`) to the ASP.NET Core process; the typed client (ADR-0006) calls **same-origin,
relative paths only** — no absolute base URL, and no CORS middleware on the backend. In **production**
(named, **not built in the slice**), the SPA is hosted on **Azure Static Web Apps with a linked
backend**, which reverse-proxies those same API routes to the Azure-hosted .NET API under the SWA
origin — keeping the browser single-origin there too *(assumption: the exact backend compute host —
App Service vs Container Apps — is deferred to when the prod path is built; it does not affect the
topology)*. This **binds the frontend's transport contract**: the typed API client uses relative paths
and assumes same-origin. Standing up backend CORS is explicitly a **non-decision** here — if a future
deployment cannot preserve single-origin, that is a new (superseding) ADR.

## Consequences
- **No CORS, anywhere.** No preflight, no origin allow-list, no cross-origin credential rules to
  maintain or debug in prod — the most common SPA↔API failure mode is designed out rather than
  configured around.
- **A clean, tellable prod story.** "Same origin in dev (Vite proxy) and prod (SWA linked backend);
  the SPA only ever calls relative paths" is a complete, conventional answer to *how does your
  frontend talk to the backend?* — the artifact this ADR exists to be.
- **Backend stays untouched by the frontend slice.** No `AddCors`/`UseCors`, no allowed-origins config
  — consistent with spec 0003's "frontend-only, no backend change" scope.
- **The typed client must use relative paths.** A hardcoded absolute API URL
  (`http://localhost:5xxx/...`) would break the model and reintroduce CORS — so the single-typed-client
  rule (ADR-0006) is also the single place this same-origin invariant is kept.
- **Route consistency across envs is a follow-on.** Dev (Vite proxy) and prod (SWA) must route the
  **same** relative paths to the API; pin the route mapping when SWA is actually wired *(assumption:
  `/documents` and `/ask` stay root-relative as spec 0003 uses; harmonize via `staticwebapp.config.json`
  at prod time)*.
- **Prod is named, not built.** SWA + linked backend, the compute host, and `staticwebapp.config.json`
  are **out of scope** for the slice (production hardening); this ADR records the intended topology,
  not an implementation.
- **Given up:** hosting the SPA on a wholly separate origin/CDN from the API *without* a reverse proxy.
  If that is ever required, CORS returns and this decision is superseded.
