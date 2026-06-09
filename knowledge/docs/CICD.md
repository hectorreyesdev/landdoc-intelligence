# CI/CD — auto-deploy on merge to `main`

A GitHub Actions workflow that rebuilds the image and rolls a new Azure Container Apps revision every
time a PR merges to `main`. Authentication is **passwordless** (OIDC federated credentials — no Azure
secrets stored in GitHub). The manual equivalent is in [DEPLOYMENT.md](DEPLOYMENT.md); the deployment
shape is [ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md).

## How it works

```
PR merged → push to main → GitHub Actions
  → azure/login (OIDC, no stored secret)
  → az acr build         (build image server-side in ACR, tag = commit sha)
  → az containerapp update --image …:<sha>   (new revision rolls out)
```

The app's **runtime secrets never touch CI**. They stay in Key Vault and are read at startup by the
container's managed identity (set up once in DEPLOYMENT.md §1c–1d). CI only swaps the image; the
identity, role grant, env vars, and ingress all persist across revisions.

## What you set up once

Three things: an Entra **identity for GitHub** (with a federated credential trusting your repo), the
**Azure roles** that identity needs, and the **GitHub values** the workflow reads. Run the Azure parts
with an account that can create app registrations and assign roles (Owner / User Access Administrator on
the subscription or RG).

### Reference values

```bash
SUBSCRIPTION="<SUBSCRIPTION_ID>"
TENANT="<TENANT_ID>"
RG="rg-landdoc-deomo"
APP="landdoc"
ACR="ca6a00db456cacr"
REPO="hectorreyesdev/landdoc-intelligence"     # owner/repo
az account set --subscription "$SUBSCRIPTION"
```

### Step 1 — create the Entra app + service principal

```bash
APP_ID=$(az ad app create --display-name "gh-landdoc-deploy" --query appId -o tsv)
az ad sp create --id "$APP_ID"
echo "AZURE_CLIENT_ID = $APP_ID"
```

### Step 2 — add a federated credential trusting `main`

This lets GitHub Actions running on `main` get a token as that app — **no client secret**.

```bash
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "gh-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:hectorreyesdev/landdoc-intelligence:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

> The `subject` must match the workflow trigger exactly. `ref:refs/heads/main` covers pushes to `main`
> (which is what a PR merge produces). To also let the workflow run from the **Actions tab** via
> `workflow_dispatch` on main, this same subject applies. If you later add environment protection, use
> `subject: repo:<owner>/<repo>:environment:<name>` instead.

### Step 3 — grant the identity the Azure roles it needs

`az acr build` needs **Contributor on the registry**; `az containerapp update` needs **Contributor on
the app**. (Simplest alternative: a single `Contributor` on the resource group — broader, since the RG
also holds the Key Vault and AI resource, so the scoped pair below is preferred.)

```bash
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)
ACR_ID=$(az acr show -n "$ACR" --query id -o tsv)
APP_RES_ID=$(az containerapp show -n "$APP" -g "$RG" --query id -o tsv)

az role assignment create --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
  --role "Contributor" --scope "$ACR_ID"          # for az acr build
az role assignment create --assignee-object-id "$SP_OBJECT_ID" --assignee-principal-type ServicePrincipal \
  --role "Contributor" --scope "$APP_RES_ID"      # for az containerapp update
```

### Step 4 — add the GitHub repo secrets

The workflow reads three non-sensitive identifiers (they're IDs, not credentials — OIDC supplies the
actual token at runtime). Set them as repo secrets:

```bash
gh secret set AZURE_CLIENT_ID       --repo "$REPO" --body "$APP_ID"
gh secret set AZURE_TENANT_ID       --repo "$REPO" --body "$TENANT"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUBSCRIPTION"
```

Or in the GitHub UI: **Settings → Secrets and variables → Actions → New repository secret**.

### Step 5 — add the workflow

Commit `.github/workflows/deploy.yml` (already added in this repo — see the file). It is:

```yaml
name: Deploy to Azure Container Apps

on:
  push:
    branches: [main]
    paths:
      - 'backend/**'
      - 'frontend/**'
      - 'Dockerfile'
      - '.dockerignore'
      - '.github/workflows/deploy.yml'
  workflow_dispatch:

permissions:
  id-token: write   # required for OIDC login
  contents: read

env:
  RESOURCE_GROUP: rg-landdoc-deomo
  CONTAINERAPP: landdoc
  ACR_NAME: ca6a00db456cacr
  IMAGE_REPO: landdoc

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Azure login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Build image in ACR
        run: |
          az acr build \
            --registry "$ACR_NAME" \
            --image "$IMAGE_REPO:${{ github.sha }}" \
            --image "$IMAGE_REPO:latest" \
            --file Dockerfile .

      - name: Deploy new revision
        run: |
          az containerapp update \
            --name "$CONTAINERAPP" \
            --resource-group "$RESOURCE_GROUP" \
            --image "$ACR_NAME.azurecr.io/$IMAGE_REPO:${{ github.sha }}"

      - name: Show URL
        run: |
          FQDN=$(az containerapp show -n "$CONTAINERAPP" -g "$RESOURCE_GROUP" \
            --query "properties.configuration.ingress.fqdn" -o tsv)
          echo "Deployed: https://$FQDN/" >> "$GITHUB_STEP_SUMMARY"
```

> `az acr build` runs the Docker build on ACR's servers, so the runner needs neither Docker nor buildx.
> The `paths:` filter skips deploys for docs/knowledge-only merges; drop it to deploy on every merge.

### Step 6 — try it

- **Real run:** merge a PR into `main` that touches `backend/`, `frontend/`, or the `Dockerfile`. Watch
  **Actions** → the run logs in, builds, and rolls a revision; the summary prints the URL.
- **Dry run without merging:** Actions tab → *Deploy to Azure Container Apps* → **Run workflow** (the
  `workflow_dispatch` trigger).
- **Confirm the rollout:**
  ```bash
  az containerapp revision list -n landdoc -g rg-landdoc-deomo \
    --query "[-1].{name:name, image:properties.template.containers[0].image, health:properties.healthState}" -o table
  ```

## Notes & options

- **Validation on PRs (optional):** add a separate workflow on `pull_request` that runs `dotnet test`
  and `npm test` (and optionally `docker build` for a build-only check) so merges to `main` are already
  green. Keep that build job free of Azure creds — only the deploy job on `main` needs OIDC.
- **Rollback:** revisions are immutable and retained. Reactivate a prior one:
  `az containerapp revision activate -n landdoc -g rg-landdoc-deomo --revision <name>`.
- **In-memory store:** each new revision starts with an empty corpus — re-upload documents after a
  deploy (by design for the slice; ADR-0005).
- **Least privilege:** the deploy identity can build images and update the app — it has **no** access to
  Key Vault secrets (those are the app's managed identity's job). Tighten further with a custom role if
  Contributor-on-ACR is too broad.
