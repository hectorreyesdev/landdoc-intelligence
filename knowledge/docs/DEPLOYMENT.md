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
| Subscription | `c3ef00c0-da7f-4e63-86ac-fee62aee44ce` |
| Resource group | `rg-landdoc-deomo` (region `eastus2`) |
| Container App | `landdoc` |
| Container Apps env | `cae-landdoc` |
| Container Registry | `ca6a00db456cacr` (`ca6a00db456cacr.azurecr.io`, repo `landdoc`) |
| Log Analytics | `workspace-rglanddocdeomoWNBf` |
| Key Vault | `kv-landdoc-hr01` (RBAC mode) |
| Public URL | https://landdoc.wittyground-3c06fff6.eastus2.azurecontainerapps.io/ |

Convenience variables used below:

```bash
SUBSCRIPTION="c3ef00c0-da7f-4e63-86ac-fee62aee44ce"
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

### 1e. Verify

```bash
FQDN=$(az containerapp show -n "$APP" -g "$RG" --query "properties.configuration.ingress.fqdn" -o tsv)
curl -s -o /dev/null -w "GET /     -> %{http_code} %{content_type}\n" "https://$FQDN/"
curl -s -o /dev/null -w "POST /ask -> %{http_code} %{content_type}\n" \
  -X POST "https://$FQDN/ask" -H "Content-Type: application/json" -d '{"question":"ping"}'
```

`GET /` → `200 text/html` (SPA). `POST /ask` → `409` (empty store) **or** `200` — either proves the
vault-supplied key reached the model. A `500` means the identity/role isn't wired (see 1c–1d).

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

> The vector store is **in-memory**, so every new revision starts with an empty corpus — re-upload
> documents after a redeploy. To change a non-secret setting without rebuilding, use
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

## 4. Teardown

The Key Vault (`kv-landdoc-hr01`) and the Azure AI resource (`landdoc-rag-resource…`) also live in
`rg-landdoc-deomo`, so **do not delete the whole resource group** unless you intend to destroy those
too. Delete only what this deployment created.

### 4a. Remove just the app and its build/runtime infra

```bash
az account set --subscription "$SUBSCRIPTION"

# the Key Vault role assignment for the app's identity (capture id before deleting the app)
PRINCIPAL_ID=$(az containerapp show -n "$APP" -g "$RG" --query "identity.principalId" -o tsv)
VAULT_ID=$(az keyvault show --name "$VAULT" --query id -o tsv)
az role assignment delete --assignee "$PRINCIPAL_ID" --role "Key Vault Secrets User" --scope "$VAULT_ID"

az containerapp delete  -n "$APP" -g "$RG" --yes        # the app (all revisions)
az containerapp env delete -n "$ENV" -g "$RG" --yes     # the environment
az acr delete -n ca6a00db456cacr -g "$RG" --yes         # the registry + images
az monitor log-analytics workspace delete \
  -g "$RG" -n workspace-rglanddocdeomoWNBf --yes        # the Log Analytics workspace
```

> The custom hostname binding and its managed certificate live on the environment, so they're removed
> when the app / env above are deleted — no separate step. The Namecheap `landdoc` CNAME + `asuid.landdoc`
> TXT are harmless to leave, or delete them in Advanced DNS.

### 4b. Nuke everything in the RG (⚠️ also deletes Key Vault + AI resource)

Only if you really want the whole environment gone:

```bash
az group delete -n "$RG" --yes --no-wait
```

> Cost note: with the app at 1 always-on replica plus ACR Basic, idle cost is ~a few USD/month. To cut
> it without tearing down, scale to zero: `az containerapp update -n "$APP" -g "$RG" --min-replicas 0`
> (adds a cold start on the first request after idle).
