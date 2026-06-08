# 0016. Single-container deployment to Azure Container Apps with secrets from Key Vault

- Status: Accepted
- Date: 2026-06-08
- Supersedes: [ADR-0011](0011-single-origin-spa-api-topology.md) — its prod realization only (SWA linked
  backend); ADR-0011's single-origin / no-CORS principle carries forward and is upheld here.

## Context
The slice now has a live cloud dependency on both paths — Azure OpenAI for chat
([[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]]) and
embeddings ([[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]])
— so running it for real needs (a) somewhere to host it on a public URL and (b) a way to feed it the
AzureOpenAI / Anthropic secrets without baking them into source or the image.

Two things are already settled and bound this decision:
- **Single-origin topology** ([[knowledge/docs/decisions/0011-single-origin-spa-api-topology]]): the
  SPA and API are served from one origin, no CORS. ADR-0011 named "an Azure Static Web Apps linked
  backend" as the prod realization; that was a placeholder, never built.
- **Secrets posture** (CLAUDE.md guardrails; ADR-0012/0013 both say *"managed identity / Key Vault in
  hosting"*): keys live in `dotnet user-secrets` for dev and were always intended to come from Key
  Vault in hosting. Adapter options already bind from the `AzureOpenAI:*` / `Anthropic:*` config
  sections; nothing about *where config comes from* is the adapters' concern.

A Key Vault (`kv-landdoc-hr01`, RBAC-mode, in `rg-landdoc-deomo`) already holds the secrets, named on
the `--` convention (`AzureOpenAI--ApiKey`, `AzureOpenAI--Endpoint`, `Anthropic--ApiKey`) that maps
directly onto the existing config keys. So the missing pieces are purely a config *source* and a
hosting target — not any interface or adapter change. Provisioning production infrastructure (VNet,
Private Link, AI Search, observability) stays out of scope per CLAUDE.md; Container Apps + Key Vault
is the minimum to put the existing slice on a public URL with managed secrets.

## Decision
We will **package the app as one container image and run it on Azure Container Apps, sourcing runtime
secrets from Azure Key Vault via `DefaultAzureCredential`** — superseding ADR-0011's "Static Web Apps
linked backend" placeholder as the realization of the single-origin topology.

- **One image, one origin.** A repo-root multi-stage `Dockerfile` builds the Vite SPA and publishes the
  ASP.NET Core API, then the runtime stage serves the SPA from `wwwroot` alongside the API on port
  `8080` (`UseDefaultFiles` / `UseStaticFiles` + `MapFallbackToFile("index.html")`; the `/documents`
  and `/ask` endpoints still match first). This *is* the single-origin shape of ADR-0011 — same-origin,
  no CORS — now in one deployable artifact.
- **Key Vault as an opt-in config source.** `Program.cs` adds the vault to configuration **only when
  `KeyVault:Uri` is set**: `builder.Configuration.AddAzureKeyVault(new Uri(uri), new DefaultAzureCredential())`.
  Vault secret names map `--` → `:`, so they overlay the existing `AzureOpenAI:*` / `Anthropic:*` keys
  with **no adapter change**. Unset (tests, offline `dotnet run`) → the source is skipped, so the suite
  stays credential-free and deterministic, consistent with the "tests pinned offline" posture of
  ADR-0012/0013.
- **One credential, two contexts.** `DefaultAzureCredential` resolves to the developer's `az login`
  locally and to the **Container App's managed identity** in Azure — identical code, no secrets in the
  image, no environment-specific branch. The identity is granted the **`Key Vault Secrets User`** RBAC
  role on the vault (the vault is RBAC-mode, not access-policy).
- **Secrets never enter source or image.** Only non-secret config travels in `appsettings.json`
  (deployment names, providers) and as plain env vars on the Container App (`KeyVault__Uri`, provider
  selection). Endpoint and keys come from the vault at startup.

This changes **deployment and configuration sourcing only** — no port, adapter, or API-contract change
(those would need a spec per CLAUDE.md). It adds two packages: `Azure.Identity`,
`Azure.Extensions.AspNetCore.Configuration.Secrets`.

## Consequences
- **The slice can run on a public HTTPS URL** with managed secrets, while staying a single process /
  single origin — no new architectural surface beyond hosting.
- **Secrets are out of source, image, and history.** Rotating a key is a vault operation; no rebuild.
  The image is publishable to a registry without leaking credentials.
- **Local dev is unchanged and still works offline.** Without `KeyVault:Uri` nothing reaches the vault;
  with it set, `az login` transparently supplies the credential — verified locally (`POST /ask` reaches
  the vault-supplied embedding model and returns 409 empty-store rather than a no-credential 500).
- **A startup dependency on Key Vault when enabled.** If `KeyVault:Uri` is set but the identity lacks
  `Key Vault Secrets User` or the vault is unreachable, configuration load fails fast at boot — the
  intended behaviour (don't start half-configured), but it makes the role grant a hard prerequisite.
- **Supersedes the ADR-0011 placeholder** for the prod path; ADR-0011's single-origin *principle* is
  unchanged and now realized by the container rather than a Static Web Apps linked backend.
- **Still out of scope** (CLAUDE.md): VNet/Private Link, AI Search, auth/RBAC on the app itself,
  observability stack. Container Apps ingress is public and unauthenticated for the demo.
- **Doc propagation:** RUNBOOK gains the container + Key Vault run/deploy steps; the stale "Static Web
  Apps linked backend" notes in `vite.config.ts` / `client.ts` / RUNBOOK are corrected to the
  single-container shape. STACK/ARCHITECTURE rows that touch hosting reconcile post-merge via `/reconcile`.

## Links
- Supersedes the prod-path placeholder in [[knowledge/docs/decisions/0011-single-origin-spa-api-topology]]
  (the single-origin principle stands).
- Builds on [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]] ·
  [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]
  (live cloud deps; "tests pinned offline" posture).
- Relates to [[knowledge/docs/AZURE-CONFIG]] (provisioned resources) and CLAUDE.md guardrails (secrets, scope).
