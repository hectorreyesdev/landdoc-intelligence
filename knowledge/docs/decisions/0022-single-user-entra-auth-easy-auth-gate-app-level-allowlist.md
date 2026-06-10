# 0022. Single-user authentication: Entra ID via Container Apps Easy Auth, with an app-level allowlist check

- Status: Accepted
- Date: 2026-06-10

## Context
The slice is live on a public URL (ADR-0016: single container on Azure Container Apps —
`https://landdoc.hectorreyes.dev/`). Anyone who finds that URL can ingest documents, delete the
corpus, and — the sharpest force — issue `/ask` requests that spend real Azure OpenAI tokens on the
owner's subscription. The original out-of-scope list (CLAUDE.md, [[knowledge/docs/PRD]], README) put
**auth/RBAC** out of scope; that posture predates the deployment — it was written for a local-only
slice, and ADR-0016 changed the facts on the ground the same way provisioning changed them for
ADR-0012. Builds on [[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]];
amends the scope posture stated in CLAUDE.md / PRD / README (no prior **Accepted** ADR claimed auth,
so nothing is superseded).

What bounds the choice:

- **Single user.** The owner is the only intended user, signing in with the Microsoft account they
  run the Azure subscription with ("my Windows account"). No roles, no user management — RBAC stays
  out of scope.
- **Vertical-slice ethos.** Simplest thing that proves the flow. A full app-level OIDC build (MSAL in
  the SPA, JWT bearer in the API) is more surface than one user needs.
- **Offline mode is load-bearing.** Local dev and the whole test suite run with zero cloud
  dependencies via config-selected providers (`ModelClient:*`, `VectorStore:Provider`,
  `DocumentStore:Provider`, `UsageSource:Provider`). Auth must follow the same pattern: enforced
  live, absent offline, swapped by config — never a code change.
- **Single origin, one container** (ADR-0011 via ADR-0016): the SPA and API share one origin, so one
  gate at the edge covers both, and the platform's login redirect happens before the SPA loads — no
  frontend work needed.
- **Defense in depth was chosen deliberately** (owner's call): the platform gate alone leaves the app
  trusting its network position; if the auth config is ever disabled, misconfigured, or the container
  is reached without the sidecar, an unauthenticated app would be silently open. A second, in-app
  check makes the API itself refuse unknown callers.

## Decision
We will bring **single-user authentication into scope** (amending the out-of-scope list: auth is in,
RBAC stays out) and implement it in two layers:

1. **Platform gate — Azure Container Apps built-in authentication ("Easy Auth") with Microsoft
   Entra ID.** An Entra app registration backs the `landdoc` Container App's auth config; every
   request must be authenticated (unauthenticated browsers are redirected to the Microsoft sign-in
   page), and the authorization policy restricts access to the owner's identity via
   `defaultAuthorizationPolicy.allowedPrincipals.identities = [<owner object ID>]`. This is Azure
   configuration, not application code.
2. **App-level allowlist check — defense in depth.** ASP.NET Core middleware validates the identity
   Easy Auth injects (`X-MS-CLIENT-PRINCIPAL-ID`) against a configured allowlist. Mode is
   config-selected like every other seam: `Auth:Mode` = `easyauth` (live — header required and
   principal must be allowlisted, else 401/403) or `none` (the default — local dev, offline mode, and
   tests, zero cloud dependency). Allowed principals come from `Auth:AllowedPrincipalIds` *(assumption:
   a list, holding one entry today — object IDs are not secrets, so this is plain config/env, not Key
   Vault)*.

The frontend is unchanged: sign-in happens at the platform edge before the SPA is served. The Entra
app registration's client secret is stored as a Container App secret *(assumption: ACA secret rather
than a Key Vault reference — it is consumed by the platform sidecar, not by app config)*.

## Consequences
- **(+) The public URL stops being anonymous.** Corpus reads/writes/deletes and LLM token spend are
  gated to the owner; the sharpest live risk of ADR-0016 is closed.
- **(+) The pattern stays consistent.** Auth becomes the sixth config-selected seam; `dotnet test` /
  `npm test` and local dev remain offline and green with `Auth:Mode=none` (the default), so no
  existing test changes.
- **(+) Zero frontend scope.** No MSAL, no login UI, no token plumbing in the SPA.
- **(+) Defense in depth.** A platform misconfiguration no longer silently exposes the app — the API
  refuses requests without an allowlisted principal when `Auth:Mode=easyauth`.
- **(−) Platform state lives outside the repo.** The Easy Auth config and app registration are Azure
  resources, not code; they must be documented in AZURE-CONFIG / DEPLOYMENT (and re-applied on
  re-provision) or they drift.
- **(−) A new credential to manage.** The app registration's client secret is a new secret with an
  expiry; rotation is a manual operator task noted in the runbook.
- **(−) Smoke checks change.** `curl` against the live URL now gets a login redirect; RUNBOOK-PROD
  smoke checks need an authenticated path (browser) or a documented bypass-free alternative.
- **(−) Probes and platform paths need care.** Container Apps health probes and any path that must
  stay reachable pre-auth have to be accounted for in the auth config (`excludedPaths`) — an
  implementation detail for the spec.
- **(neutral) RBAC, multi-user, and app-level OIDC remain out of scope.** If a second user or
  machine-to-machine access ever appears, that is a new ADR (likely growing the app-level layer into
  real JWT bearer auth).
- **Follow-on:** a feature spec (`knowledge/docs/specs/`) for the app-level middleware + the
  deployment/config steps; scope-posture edits to CLAUDE.md, PRD, README on acceptance.
