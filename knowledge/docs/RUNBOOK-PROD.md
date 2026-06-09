# Runbook — production environment

Operating the live deployment of LandDoc Intelligence on **Azure Container Apps** (single container: SPA +
API on one origin, port 8080; secrets from Key Vault via managed identity). This is the day-to-day
operator's guide. For first-time provisioning and the full CLI walkthrough see
[DEPLOYMENT.md](DEPLOYMENT.md); for the CI/CD identity setup see [CICD.md](CICD.md). Design rationale:
[ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).

## The live environment
| Thing | Value |
|---|---|
| Public URL | <https://landdoc.hectorreyes.dev/> (also the ACA FQDN `landdoc.wittyground-3c06fff6.eastus2.azurecontainerapps.io`) |
| Subscription | `<SUBSCRIPTION_ID>` |
| Resource group | `rg-landdoc-deomo` (`eastus2`) |
| Container App | `landdoc` (env `cae-landdoc`) |
| Registry | `ca6a00db456cacr.azurecr.io` (repo `landdoc`) |
| Key Vault | `kv-landdoc-hr01` (RBAC) |
| Storage (documents) | `stlanddochr01` → container `documents` |

```bash
SUBSCRIPTION="<SUBSCRIPTION_ID>"
RG="rg-landdoc-deomo"; APP="landdoc"; ENV="cae-landdoc"; ACR="ca6a00db456cacr"
az account set --subscription "$SUBSCRIPTION"
```

The app reads its runtime secrets (Azure OpenAI, Anthropic, Search, Blob endpoint) from Key Vault at
startup as its **system-assigned managed identity** — no secrets in the image, in CI, or in env vars. The
identity, role grants, env vars, and ingress all **persist across revisions**; a deploy only swaps the
image. With the live providers (Azure AI Search + Blob) the corpus **persists across revisions/redeploys**.

## Deploy

### Automatic — CD on merge to `main` (the default path)
A GitHub Actions workflow (`.github/workflows/deploy.yml`) deploys on every push to `main` that touches
`backend/**`, `frontend/**`, `Dockerfile`, `.dockerignore`, or the workflow file. So **merging a PR ships
it** — no manual step. The job logs in with OIDC (no stored Azure secret), runs `az acr build` (image
tagged with the commit SHA + `latest`), then `az containerapp update` to roll a new revision, and prints
the URL in the run summary. Docs-only / `knowledge/`-only merges are skipped by the `paths:` filter.

- **Watch a deploy:** GitHub → **Actions** → *Deploy to Azure Container Apps*.
- **Trigger without merging** (e.g. redeploy current `main`): Actions → that workflow → **Run workflow**
  (`workflow_dispatch`).
- Setup (one-time, already done): federated identity + role grants + repo secrets — see [CICD.md](CICD.md).

### Manual — from your machine
For a hotfix when CI is unavailable, or to deploy an un-merged working tree. Requires `az login` with rights
on the RG and the `containerapp` extension. The identity/Key Vault wiring is already in place — **do not**
repeat it; you're only rolling a new image.

```bash
az account set --subscription "$SUBSCRIPTION"

# One-step: build in the cloud (ACR Tasks — no local Docker) + roll a revision from the current tree
az containerapp up -n "$APP" -g "$RG" --environment "$ENV" --source .
```

Or the explicit build-then-update (mirrors what CI does, lets you pin a tag):
```bash
TAG=$(git rev-parse --short HEAD)
az acr build --registry "$ACR" --image "landdoc:$TAG" --file Dockerfile .
az containerapp update -n "$APP" -g "$RG" --image "$ACR.azurecr.io/landdoc:$TAG"
```

Full detail + custom-domain steps: [DEPLOYMENT.md § 2](DEPLOYMENT.md).

## Verify a rollout
```bash
# newest revision health
az containerapp revision list -n "$APP" -g "$RG" \
  --query "[-1].{name:name, image:properties.template.containers[0].image, active:properties.active, health:properties.healthState}" -o table

# end-to-end smoke
FQDN=$(az containerapp show -n "$APP" -g "$RG" --query "properties.configuration.ingress.fqdn" -o tsv)
curl -s -o /dev/null -w "GET /          -> %{http_code} %{content_type}\n" "https://$FQDN/"
curl -s -o /dev/null -w "GET /documents -> %{http_code} %{content_type}\n" "https://$FQDN/documents"
curl -s -o /dev/null -w "POST /ask      -> %{http_code}\n" -X POST "https://$FQDN/ask" \
  -H "Content-Type: application/json" -d '{"question":"ping"}'
```
Healthy: `GET /` → `200 text/html`; `GET /documents` → `200 application/json`; `POST /ask` → `409` (empty)
or `200`. A `500` on `/ask` ⇒ Key Vault identity/role not wired; a `500` on `/documents` ⇒ blob identity/role
not wired (see [DEPLOYMENT.md § 1c–1e](DEPLOYMENT.md)).

## Day-2 operations

**Logs**
```bash
az containerapp logs show -n "$APP" -g "$RG" --follow --tail 100          # live stream
az containerapp logs show -n "$APP" -g "$RG" --type system --tail 50      # platform/system events
```
Deeper history is in the `workspace-rglanddocdeomoWNBf` Log Analytics workspace (ContainerAppConsoleLogs_CL).

**Usage & cost** — LLM token usage and spend are read from **Azure Monitor platform metrics** on the
Foundry resource (emitted automatically, free, no app code) — see
[ADR-0020](decisions/0020-llm-usage-cost-observability-azure-monitor-metrics.md).

**Restart the current revision** (e.g. after granting a role, or to clear state):
```bash
az containerapp revision restart -n "$APP" -g "$RG" \
  --revision "$(az containerapp revision list -n "$APP" -g "$RG" --query '[-1].name' -o tsv)"
```

**Rollback** — revisions are immutable and retained; reactivate a known-good one:
```bash
az containerapp revision list -n "$APP" -g "$RG" --query "[].{name:name,created:properties.createdTime,active:properties.active}" -o table
az containerapp revision activate -n "$APP" -g "$RG" --revision <good-revision-name>
```

**Scale** — currently pinned at 1 always-on replica. To cut idle cost (~a few USD/mo), scale to zero
(adds a cold start on the first request after idle):
```bash
az containerapp update -n "$APP" -g "$RG" --min-replicas 0       # or back to 1
```

**Change a non-secret setting** (no rebuild): `az containerapp update -n "$APP" -g "$RG" --set-env-vars KEY=VALUE`.

**Rotate a secret** — update it in Key Vault, then restart the revision so it re-reads at startup (no rebuild):
```bash
az keyvault secret set --vault-name kv-landdoc-hr01 --name AzureOpenAI--ApiKey --value "<new>"
az containerapp revision restart -n "$APP" -g "$RG" --revision "$(az containerapp revision list -n "$APP" -g "$RG" --query '[-1].name' -o tsv)"
```
(Secret names use `--` for `:`, e.g. `AzureOpenAI--ApiKey`, `Search--ApiKey`, `Blob--ServiceUri`.)

## Cost & teardown
Idle cost ≈ a few USD/month (1 always-on replica + ACR Basic). Scale to zero (above) to trim without tearing
down. To remove resources, follow [DEPLOYMENT.md § 4](DEPLOYMENT.md) — delete **only what this deployment
created** (app, env, registry, Log Analytics + the app-identity role assignments). The Key Vault, Azure AI
resource, Azure AI Search, and the `stlanddochr01` storage account also live in `rg-landdoc-deomo`, so **do
not `az group delete` the whole RG** unless you intend to destroy those too.
