# Deployment — Azure Container Apps

How to deploy LandDoc Intelligence to Azure as a single container (SPA + API, one origin, port 8080)
with secrets pulled from Key Vault via a managed identity. This is the manual / CLI path; for
automated deploys on PR merge see [CICD.md](CICD.md). Design rationale: [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).

> All commands assume Azure CLI ≥ 2.87 and that you're logged in to the right subscription.
> Run them from the **repo root** (the build context). Secret *values* never appear here — they live
> only in Key Vault `kv-landdoc-hr01`.

## Environment (the deployed resources)

| Thing | Value |
|---|---|
| Subscription | `<SUBSCRIPTION_ID>` |
| Resource group | `rg-landdoc-deomo` (region `eastus2`) |
| Container App | `landdoc` |
| Container Apps env | `cae-landdoc` |
| Container Registry | `ca6a00db456cacr` (`ca6a00db456cacr.azurecr.io`, repo `landdoc`) |
| Log Analytics | `workspace-rglanddocdeomoWNBf` |
| Key Vault | `kv-landdoc-hr01` (RBAC mode) |
| Public URL | https://landdoc.wittyground-3c06fff6.eastus2.azurecontainerapps.io/ |

Convenience variables used below:

```bash
SUBSCRIPTION="<SUBSCRIPTION_ID>"
RG="rg-landdoc-deomo"
LOCATION="eastus2"
APP="landdoc"
ENV="cae-landdoc"
VAULT="kv-landdoc-hr01"
VAULT_URI="https://kv-landdoc-hr01.vault.azure.net/"

az account set --subscription "$SUBSCRIPTION"
```

---

## 1. First-time deploy (from nothing)

Provisions the registry, environment, and app, then wires the managed identity to Key Vault. The app
boots **without** vault access first (so the initial revision starts cleanly), then gets the identity +
role, then a revision that reads the vault.

### 1a. Prerequisites (one-time per machine)

```bash
az login                                   # interactive
az account set --subscription "$SUBSCRIPTION"
az extension add --name containerapp --upgrade --allow-preview true
az provider register -n Microsoft.App --wait
az provider register -n Microsoft.OperationalInsights --wait
az provider register -n Microsoft.ContainerRegistry --wait
```

### 1b. Build + deploy the container

`az containerapp up --source .` builds the image in the cloud (ACR Tasks — no local Docker needed),
creates the ACR + environment on first run, and deploys. Note: **no `KeyVault__Uri` yet** — that comes
after the identity exists.

```bash
az containerapp up \
  -n "$APP" -g "$RG" \
  --location "$LOCATION" \
  --environment "$ENV" \
  --source . \
  --ingress external \
  --target-port 8080 \
  --env-vars ModelClient__ChatProvider=azureopenai ModelClient__EmbeddingProvider=azureopenai
```

### 1c. Give the app a managed identity and Key Vault access

```bash
# system-assigned identity → capture its principal id
PRINCIPAL_ID=$(az containerapp identity assign \
  -n "$APP" -g "$RG" --system-assigned --query principalId -o tsv)

VAULT_ID=$(az keyvault show --name "$VAULT" --query id -o tsv)

# grant least-privilege read on secrets (vault is RBAC-mode)
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --scope "$VAULT_ID"
```

### 1d. Turn on Key Vault + always-on, rolling a fresh revision

```bash
az containerapp update \
  -n "$APP" -g "$RG" \
  --set-env-vars KeyVault__Uri="$VAULT_URI" \
  --min-replicas 1 --max-replicas 1
```

> RBAC can take a minute to propagate. If the new revision boots before the role lands, restart it:
> `az containerapp revision restart -n "$APP" -g "$RG" --revision <latest>`.

### 1e. Configure the Blob document store (spec 0006 / ADR-0018)

The documents table + original-file viewer persist each upload (bytes + metadata) to the `documents`
container on the **`stlanddochr01`** storage account. The container already exists (see
[AZURE-CONFIG.md §2](AZURE-CONFIG.md)); wire the app to it in three steps.

**Step 1 — grant the app's managed identity blob access** (passwordless path; the app reads/writes blobs
as its identity, no key in config):

```bash
STORAGE_ID=$(az storage account show -n stlanddochr01 -g "$RG" --query id -o tsv)
az role assignment create \
  --role "Storage Blob Data Contributor" \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --scope "$STORAGE_ID"
```

**Step 2 — supply the blob endpoint via Key Vault** (consistent with the other endpoints — the Key Vault
config source loads it automatically as `Blob:ServiceUri`, which triggers the managed-identity path).
`DocumentStore:Provider=azureblob` is the committed `appsettings.json` default, so no env var is needed —
only the secret:

```bash
az keyvault secret set --vault-name "$VAULT" --name Blob--ServiceUri \
  --value "https://stlanddochr01.blob.core.windows.net"
```

**Step 3 — let RBAC propagate, then restart the revision so it picks up the grant** (the adapter creates
the `documents` container on startup if it's missing — idempotent):

```bash
az containerapp revision restart -n "$APP" -g "$RG" \
  --revision "$(az containerapp revision list -n "$APP" -g "$RG" --query '[-1].name' -o tsv)"
```

> **Connection-string alternative (no role grant):** instead of Steps 1–2, set
> `DocumentStore__Provider=azureblob` and supply `Blob__ConnectionString` from the `Blob--ConnectionString`
> Key Vault secret. Managed identity (above) is preferred (ADR-0016 — no secret in play). For **local dev**
> with no Azure, set `DocumentStore__Provider=inmemory` (or run Azurite and use `Blob__ConnectionString`).

### 1f. Verify

```bash
FQDN=$(az containerapp show -n "$APP" -g "$RG" --query "properties.configuration.ingress.fqdn" -o tsv)
curl -s -o /dev/null -w "GET /          -> %{http_code} %{content_type}\n" "https://$FQDN/"
curl -s -o /dev/null -w "GET /documents -> %{http_code} %{content_type}\n" "https://$FQDN/documents"
curl -s -o /dev/null -w "POST /ask      -> %{http_code} %{content_type}\n" \
  -X POST "https://$FQDN/ask" -H "Content-Type: application/json" -d '{"question":"ping"}'
```

`GET /` → `200 text/html` (SPA). `GET /documents` → `200 application/json` (`[]` until something is
ingested) — proves the blob document store is reachable. `POST /ask` → `409` (empty store) **or** `200` —
either proves the vault-supplied key reached the model. A `500` on `/ask` means the Key Vault
identity/role isn't wired (see 1c–1d); a `500` on `/documents` means the blob identity/role isn't wired
(see 1e).

### 1g. Configure the LLM usage dashboard (spec 0009 / ADR-0020)

`GET /usage` (the Ops / Usage tab) reads **Azure Monitor platform metrics** for the Foundry resource via the
app's managed identity. No secret is involved — just one read-only role grant and two non-secret config keys.
Full guide (how it works · keys · local dev): [USAGE-DASHBOARD.md](USAGE-DASHBOARD.md).

> **Already applied to the live env (2026-06-09):** the grant + `Monitor__ResourceId` below are in place
> (AZURE-CONFIG §6.5/§9). The steps are idempotent — re-running is safe. The `/usage` endpoint itself goes
> live when the feature merges to `main` and CI/CD redeploys.

**Step 1 — grant the app's managed identity read access to the Foundry resource's metrics** (read-only,
least privilege):

```bash
FOUNDRY_ID=$(az cognitiveservices account show -n landdoc-rag-resource -g "$RG" --query id -o tsv)
az role assignment create \
  --role "Monitoring Reader" \
  --assignee-object-id "$PRINCIPAL_ID" \
  --assignee-principal-type ServicePrincipal \
  --scope "$FOUNDRY_ID"
```

**Step 2 — point the app at the resource + set prices** (both **non-secret** — env vars, not Key Vault).
`UsageSource:Provider=azuremonitor` is the committed `appsettings.json` default; the adapter throws fast if
`Monitor:ResourceId` is unset, so supply it (and optionally override the example price table):

```bash
az containerapp update -n "$APP" -g "$RG" \
  --set-env-vars "Monitor__ResourceId=$FOUNDRY_ID"
# Prices ship as example rates in appsettings.json; override per deployment if needed, e.g.:
#   Pricing__gpt-5.4-mini__InputPer1K=0.00015  Pricing__gpt-5.4-mini__OutputPer1K=0.0006
```

> Verify: `curl -s -o /dev/null -w "%{http_code}\n" "https://$FQDN/usage"` → `200` (zeros until there's
> metric data; **never** `500`). A `500` on `/usage` means the Monitoring Reader grant or `Monitor__ResourceId`
> isn't wired. For **local dev / CI** with no Azure Monitor, set `UsageSource__Provider=inmemory`.

---

## 2. Redeploy after code changes

The registry, environment, app, managed identity, role grant, and env vars all **persist across
revisions**. A redeploy just builds a new image and rolls a new revision — you do **not** repeat the
identity/Key Vault steps.

```bash
az account set --subscription "$SUBSCRIPTION"

# rebuild from the current working tree and deploy a new revision in one step
az containerapp up \
  -n "$APP" -g "$RG" \
  --environment "$ENV" \
  --source .
```

Or, if you prefer an explicit build-then-update (this is also what CI does):

```bash
TAG=$(git rev-parse --short HEAD)        # or any unique tag
az acr build --registry ca6a00db456cacr --image "landdoc:$TAG" --file Dockerfile .
az containerapp update -n "$APP" -g "$RG" \
  --image "ca6a00db456cacr.azurecr.io/landdoc:$TAG"
```

Check the rollout, then re-run the verify curls from 1e:

```bash
az containerapp revision list -n "$APP" -g "$RG" \
  --query "[-1].{name:name, active:properties.active, running:properties.runningState, health:properties.healthState}" -o table
```

> With the live providers (Azure AI Search chunks — ADR-0017; Azure Blob documents — ADR-0018) the corpus
> **persists across revisions/redeploys** — no re-upload needed. (Only the offline `inmemory` providers
> start empty.) To change a non-secret setting without rebuilding, use
> `az containerapp update --set-env-vars KEY=VALUE`. To change a *secret*, update it in Key Vault and
> restart the revision — no rebuild.

---

## 3. Custom domain (`landdoc.hectorreyes.dev`)

Binds a custom domain to the Container App with a **free, auto-renewing managed TLS certificate**. DNS
for `hectorreyes.dev` is at **Namecheap**; Azure issues the cert once it can verify domain ownership.
Requires external ingress (already on) plus the app's FQDN and a per-app verification id.

### 3a. Get the Azure-side values

```bash
az containerapp show -n "$APP" -g "$RG" \
  --query "{fqdn:properties.configuration.ingress.fqdn, verificationId:properties.customDomainVerificationId}" -o json
```

### 3b. Add two DNS records at Namecheap (Advanced DNS)

Host is the **subdomain part only** — Namecheap appends the zone.

| Type | Host | Value |
|---|---|---|
| CNAME | `landdoc` | the app FQDN from 3a (`landdoc.<env-suffix>.eastus2.azurecontainerapps.io`) |
| TXT | `asuid.landdoc` | the `verificationId` from 3a |

> ⚠️ Add **only** these two — leave existing M365 / email records (MX, SPF TXT) untouched, and make sure
> no URL-redirect / parking record sits on the `landdoc` host (it conflicts with the CNAME). The `asuid`
> TXT proves ownership so Azure will issue the managed cert. These records affect only the `landdoc`
> host — `www` and the apex are unaffected.

### 3c. Verify propagation, then add + bind in Azure

```bash
dig +short CNAME landdoc.hectorreyes.dev          # → the app FQDN
dig +short TXT  asuid.landdoc.hectorreyes.dev     # → the verification id

az containerapp hostname add  --hostname landdoc.hectorreyes.dev -n "$APP" -g "$RG"
az containerapp hostname bind --hostname landdoc.hectorreyes.dev -n "$APP" -g "$RG" \
  --environment "$ENV" --validation-method CNAME   # creates + binds the managed cert (up to ~20 min)
```

`bind` returns `bindingType: SniEnabled` when TLS is active. Confirm end-to-end:

```bash
curl -s -o /dev/null -w "%{http_code} ssl=%{ssl_verify_result}\n" https://landdoc.hectorreyes.dev/   # → 200 ssl=0
```

> **Live binding:** managed cert `mc-cae-landdoc-landdoc-hectorre-8517` (GeoTrust/DigiCert), bound
> `SniEnabled` on 2026-06-08, valid through 2026-12-08, **auto-renewing** — nothing to maintain.

---

## 4. Single-user authentication (Easy Auth — spec 0013 / ADR-0022)

The live URL is gated to the owner's Microsoft account, two layers (see
[ADR-0022](decisions/0022-single-user-entra-auth-easy-auth-gate-app-level-allowlist.md)): the
platform gate (Container Apps built-in auth, Entra ID) and the app-level allowlist middleware
(`Auth:Mode=easyauth`). All of this is **Azure state, not code** — re-apply it after a from-scratch
re-provision. As built: app registration **`landdoc-easyauth`** (client id
`8659ebef-c33b-4895-a228-dcb4838404c7`), owner object id `96b6d850-0233-4865-a8aa-68249d3c675b`.

```bash
# 4a. App registration — single-tenant, ID tokens on, Easy Auth callbacks for BOTH hosts
APP_ID=$(az ad app create --display-name "landdoc-easyauth" --sign-in-audience AzureADMyOrg \
  --web-redirect-uris \
    "https://landdoc.hectorreyes.dev/.auth/login/aad/callback" \
    "https://landdoc.wittyground-3c06fff6.eastus2.azurecontainerapps.io/.auth/login/aad/callback" \
  --enable-id-token-issuance true --query appId -o tsv)

# 4b. Client secret (expires — see RUNBOOK-PROD rotation note) → stored as an ACA secret, not Key
#     Vault: it's consumed by the platform auth sidecar, not by app config (ADR-0022)
SECRET=$(az ad app credential reset --id "$APP_ID" --display-name easyauth-aca --years 2 --query password -o tsv)
az containerapp secret set -g rg-landdoc-deomo -n landdoc \
  --secrets "microsoft-provider-authentication-secret=$SECRET"

# 4c. Enable Easy Auth — Entra provider + redirect unauthenticated browsers to Microsoft sign-in
TENANT=$(az account show --query tenantId -o tsv)
az containerapp auth microsoft update -g rg-landdoc-deomo -n landdoc --client-id "$APP_ID" \
  --client-secret-name microsoft-provider-authentication-secret \
  --issuer "https://login.microsoftonline.com/$TENANT/v2.0" --yes
az containerapp auth update -g rg-landdoc-deomo -n landdoc --enabled true \
  --action RedirectToLoginPage --redirect-provider azureactivedirectory

# 4d. Platform allowlist — restrict to the owner's object id. Not exposed by the CLI: GET the
#     authConfig, merge validation.defaultAuthorizationPolicy.allowedPrincipals.identities,
#     PUT it back (PATCH returns Method Not Allowed on authConfigs).
OWNER_OID=$(az ad signed-in-user show --query id -o tsv)
SUB=$(az account show --query id -o tsv)
URI="https://management.azure.com/subscriptions/$SUB/resourceGroups/rg-landdoc-deomo/providers/Microsoft.App/containerApps/landdoc/authConfigs/current?api-version=2024-03-01"
# merge step: take the GET body's `properties`, set identityProviders.azureActiveDirectory.validation
#   .defaultAuthorizationPolicy.allowedPrincipals.identities = ["$OWNER_OID"], PUT {"properties": …}
az rest --method get --uri "$URI"   # → merge → az rest --method put --uri "$URI" --body @merged.json

# 4e. Activate the app-level check (defense in depth — rolls a revision)
az containerapp update -g rg-landdoc-deomo -n landdoc \
  --set-env-vars Auth__Mode=easyauth "Auth__AllowedPrincipalIds__0=$OWNER_OID"
```

**Verify:** anonymous `curl https://landdoc.hectorreyes.dev/documents` → **401** (no data); a browser
hitting `/` → **302** to `login.microsoftonline.com`; the owner signs in and the app works end to end;
any other signed-in account → **403**. Note: the §3c `curl …/ → 200` check predates auth — anonymous
curl now returning 401 there is the expected result.

---

## 5. Teardown

The Key Vault (`kv-landdoc-hr01`), the Azure AI resource (`landdoc-rag-resource…`), the Azure AI Search
service, and the storage account (`stlanddochr01`, which holds the ingested documents) also live in
`rg-landdoc-deomo`, so **do not delete the whole resource group** unless you intend to destroy those
too. Delete only what this deployment created.

### 5a. Remove just the app and its build/runtime infra

```bash
az account set --subscription "$SUBSCRIPTION"

# the role assignments for the app's identity (capture id before deleting the app)
PRINCIPAL_ID=$(az containerapp show -n "$APP" -g "$RG" --query "identity.principalId" -o tsv)
VAULT_ID=$(az keyvault show --name "$VAULT" --query id -o tsv)
STORAGE_ID=$(az storage account show -n stlanddochr01 -g "$RG" --query id -o tsv)
FOUNDRY_ID=$(az cognitiveservices account show -n landdoc-rag-resource -g "$RG" --query id -o tsv)
az role assignment delete --assignee "$PRINCIPAL_ID" --role "Key Vault Secrets User" --scope "$VAULT_ID"
az role assignment delete --assignee "$PRINCIPAL_ID" --role "Storage Blob Data Contributor" --scope "$STORAGE_ID"
az role assignment delete --assignee "$PRINCIPAL_ID" --role "Monitoring Reader" --scope "$FOUNDRY_ID"  # usage dashboard (§1g)

az ad app delete --id 8659ebef-c33b-4895-a228-dcb4838404c7  # the Easy Auth app registration (§4)
az containerapp delete  -n "$APP" -g "$RG" --yes        # the app (all revisions)
az containerapp env delete -n "$ENV" -g "$RG" --yes     # the environment
az acr delete -n ca6a00db456cacr -g "$RG" --yes         # the registry + images
az monitor log-analytics workspace delete \
  -g "$RG" -n workspace-rglanddocdeomoWNBf --yes        # the Log Analytics workspace
```

> The custom hostname binding and its managed certificate live on the environment, so they're removed
> when the app / env above are deleted — no separate step. The Namecheap `landdoc` CNAME + `asuid.landdoc`
> TXT are harmless to leave, or delete them in Advanced DNS.

### 5b. Nuke everything in the RG (⚠️ also deletes Key Vault + AI resource)

Only if you really want the whole environment gone:

```bash
az group delete -n "$RG" --yes --no-wait
```

> Cost note: with the app at 1 always-on replica plus ACR Basic, idle cost is ~a few USD/month. To cut
> it without tearing down, scale to zero: `az containerapp update -n "$APP" -g "$RG" --min-replicas 0`
> (adds a cold start on the first request after idle).
