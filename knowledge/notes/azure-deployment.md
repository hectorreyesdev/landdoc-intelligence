# Azure deployment — single container on Container Apps, secrets from Key Vault

How this slice runs in the cloud, and the transferable bits worth reusing. The full operational
commands live in [[knowledge/docs/DEPLOYMENT]] and [[knowledge/docs/CICD]]; the decision is
[[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]]. This note is
the *why it's shaped this way* so I don't relearn it.

## The shape
One Docker image (multi-stage: build the Vite SPA, publish the .NET API, copy the SPA into the API's
`wwwroot`) serves **SPA + API on one origin, port 8080** — same single-origin/no-CORS contract as the
dev Vite proxy, just realized differently. Runs on **Azure Container Apps**. This is the prod
realization of [[knowledge/docs/decisions/0011-single-origin-spa-api-topology]] (which named SWA + a
linked backend; ADR-0016 replaced that mechanism, kept the principle).

## Secrets: one credential, two worlds
`builder.Configuration.AddAzureKeyVault(uri, new DefaultAzureCredential())`, added **only when
`KeyVault:Uri` is set** (so tests/offline runs need no cloud). The win is `DefaultAzureCredential`:
- **locally** it resolves to my `az login`,
- **in Azure** to the Container App's **system-assigned managed identity** —

…same code, no env branch, no secret in the image or env vars. Vault secret names use the `--`
convention (`AzureOpenAI--ApiKey` → config `AzureOpenAI:ApiKey`), so they overlay existing config keys
and the model adapters never change. The app's MI gets the **`Key Vault Secrets User`** RBAC role on
the vault (vault is RBAC-mode, not access-policy).

## CI/CD: passwordless, secrets never touch it
GitHub Actions on push to `main` → `az acr build` (server-side image build) → `az containerapp update`.
Auth is **OIDC federated credentials** — an Entra app with a federated credential whose `subject`
matches the workflow's branch ref (`repo:<owner>/<repo>:ref:refs/heads/main`); GitHub gets a short-lived
token, **no client secret stored**. Only three non-sensitive IDs live as repo secrets
(`AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID`). Runtime secrets stay in Key Vault — CI
only swaps the image; the running app's MI reads the vault. Clean privilege split: the CI identity can
build+deploy but **can't read secrets**.

## Gotchas I hit (see also [[knowledge/lessons]])
- **Order of operations:** deploy the app *before* granting the KV role. `az containerapp up` starts it
  immediately; a revision that boots with `KeyVault:Uri` set but no role (or before RBAC propagates)
  fails at config load. So: deploy without `KeyVault__Uri` → assign MI + grant role → add
  `KeyVault__Uri` via `update` (rolls a fresh revision once access exists).
- **`az acr build` needs `Contributor` on the registry**, not `AcrPush` — it schedules a server-side run.
- **Cheap verification:** with secrets wired, `POST /ask` returns **409 empty-store** (not a 500); that
  409 proves the embedding call reached the model with the vault key, no corpus needed.
- **In-memory vector store** ([[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]):
  every new revision starts empty — re-upload after each deploy.

## Teardown
The Key Vault + AI resource share the RG (`rg-landdoc-deomo`), so `az group delete` nukes them too —
use the targeted deletes in [[knowledge/docs/DEPLOYMENT]] §3 to keep them, and remember the CI Entra
app (`gh-landdoc-deploy`) lives in Entra, not the RG.
