# 0013 — Single-User Auth: Easy Auth Gate + App-Level Allowlist

**Status:** Accepted

## What to build
Gate the deployed app to its owner. Today the live URL (ADR-0016) is anonymous: anyone can read,
ingest, or delete documents and spend Azure OpenAI tokens via `/ask`. Per
[[knowledge/docs/decisions/0022-single-user-entra-auth-easy-auth-gate-app-level-allowlist]], access
becomes single-user in two layers: the platform signs the user in, and the app independently refuses
anyone who isn't the owner.

**Layer 1 — platform (Azure config, no code):** enable Container Apps built-in authentication
("Easy Auth") on the `landdoc` Container App with Microsoft Entra ID as the identity provider,
backed by a new Entra app registration. Unauthenticated browser requests are redirected to the
Microsoft sign-in page; the authorization policy allowlists exactly one identity (the owner's object
ID) via `defaultAuthorizationPolicy.allowedPrincipals.identities`. The deployment steps land in
DEPLOYMENT.md / AZURE-CONFIG.md so the setup is reproducible.

**Layer 2 — app (backend code):** a small ASP.NET Core middleware validates the principal Easy Auth
injects (`X-MS-CLIENT-PRINCIPAL-ID`) on every request — API routes and SPA static files alike. Mode
is config-selected like every other seam: `Auth:Mode = easyauth` (live) enforces the check;
`Auth:Mode = none` (the default) disables it, keeping local dev, offline mode, and the test suite
exactly as they are. No frontend changes: sign-in happens at the platform edge before the SPA is
served.

## Constraints
- **Backend only** (`/backend`, .NET 10 Web API). No frontend changes; no new endpoints; no changes
  to any existing port (`IChatClient` / `IEmbeddingClient` / `IVectorStore` / `IDocumentStore` /
  `IUsageSource`).
- **Config seam, never a code change** (CLAUDE.md): new `AuthOptions` bound from the `Auth` section,
  following the existing `Configure<XOptions>(GetSection("X"))` pattern in `Program.cs`:
  - `Auth:Mode` — `none` (default) | `easyauth`.
  - `Auth:AllowedPrincipalIds` — list of Entra object IDs (one entry today). Object IDs are not
    secrets: plain config / Container App env var, **not** Key Vault.
- **Middleware semantics when `Mode=easyauth`:** missing/empty `X-MS-CLIENT-PRINCIPAL-ID` → **401**;
  present but not in the allowlist → **403**; allowlisted → pass through. `Mode=easyauth` with an
  empty allowlist is a misconfiguration → fail fast at startup (validate-and-throw-early
  convention). When `Mode=none`, the middleware is a no-op *(assumption: still registered but
  short-circuiting, so the pipeline shape is identical in both modes)*.
- **Trust boundary:** the app never parses the full `X-MS-CLIENT-PRINCIPAL` claims blob or validates
  tokens itself — Easy Auth owns authentication; the app only checks *which* authenticated principal
  arrived. Real JWT validation is explicitly out of scope (a future ADR if multi-user/M2M ever
  appears).
- **Azure side:** one new Entra app registration (single-tenant; client secret stored as a Container
  App secret *(assumption: ACA secret, not Key Vault — consumed by the platform sidecar)*); Easy
  Auth configured with redirect-to-login for unauthenticated requests; `Auth__Mode=easyauth` and
  `Auth__AllowedPrincipalIds__0=<owner object ID>` set as Container App env vars. Current probes are
  ACA TCP defaults, so no path exclusions are needed *(assumption: no HTTP health probes exist —
  verified in Program.cs/DEPLOYMENT.md)*.
- **Out of scope:** RBAC, roles, multi-user, sign-out UI, MSAL/SPA token plumbing, app-level OIDC/JWT
  validation, any change to local/offline behavior or existing tests.

## How to verify
Offline (CI gate — `dotnet test`):
- With `Auth:Mode=none` (default): all existing tests pass unchanged; a request with no auth headers
  succeeds (current behavior preserved).
- With `Auth:Mode=easyauth` (via `WebApplicationFactory` config override):
  - no `X-MS-CLIENT-PRINCIPAL-ID` header → **401** on an API route **and** on `/` (SPA shell);
  - header with a non-allowlisted object ID → **403**;
  - header with an allowlisted object ID → the request reaches the endpoint (e.g. `GET /documents`
    returns 200).
- `Auth:Mode=easyauth` + empty `Auth:AllowedPrincipalIds` → the app fails to start (config
  validation throws).

Live (manual, post-deploy):
- An incognito browser hitting `https://landdoc.hectorreyes.dev/` is redirected to Microsoft sign-in;
  after signing in as the owner, the app loads and upload → ask → cited answer works end to end.
- A signed-in **non-owner** Microsoft account gets a 403 (platform policy), not the app.
- `curl https://landdoc.hectorreyes.dev/documents` (no session) returns a redirect/401 — not document
  JSON.
- The setup steps in DEPLOYMENT.md / AZURE-CONFIG.md are sufficient to re-create the app
  registration + Easy Auth config from scratch.

## Links
- Decision: [[knowledge/docs/decisions/0022-single-user-entra-auth-easy-auth-gate-app-level-allowlist]]
- Builds on: [[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]]
- Affected docs: ARCHITECTURE (cross-cutting AuthN/AuthZ — already linked), RUNBOOK-PROD (smoke
  checks change behind sign-in; secret rotation note), DEPLOYMENT (Easy Auth + app registration
  steps), AZURE-CONFIG (new registration, env vars, ACA secret), API (error model: 401/403 layer),
  GLOSSARY (Easy Auth, principal/object ID).
- Implementing PR: [#52](https://github.com/hectorreyesdev/landdoc-intelligence/pull/52) (app layer);
  Azure-side config tracked in issue [#50](https://github.com/hectorreyesdev/landdoc-intelligence/issues/50),
  docs in [#51](https://github.com/hectorreyesdev/landdoc-intelligence/issues/51)
