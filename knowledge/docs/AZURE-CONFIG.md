# landdoc-intelligence — Azure / Foundry configuration handoff

> **What this is:** the single source of truth for the live Azure + Microsoft Foundry stack standing up
> behind [[landdoc-intelligence]]. Snapshot for the **builder session** (adapter wiring) and the **azure
> lane** deploy. Mentor-side state — lives in the vault; copy the relevant block into the builder as a prompt.
> **Status:** Phase B (stack provisioned) ✅ · Phase D (hosted deploy) ✅. **As of 2026-06-08.**
>
> 🔒 **No secret VALUES in this file.** Only Key Vault secret *names* + how to fetch. Keys live in Key Vault;
> hosted apps read them via **managed identity**, local dev via **`dotnet user-secrets`** — never hardcode.
>
> ⚠️ **Confirm-from-portal** items are tagged `‹confirm›` — verify in the resource's *Keys & Endpoint* blade
> before relying on them (I recorded names from the build log, not a live `az` read).

---

## 1. Subscription / scope

| Field | Value | Note |
|---|---|---|
| Plan | **PAYG** (Pay-As-You-Go) | No hard spending cap on PAYG → protection is the RG budget + alerts (below) |
| Resource group | **`rg-landdoc-deomo`** | ⚠️ Name has a **typo** (`deomo`, not `demo`) — it's cosmetic; **use it verbatim** in every command/teardown, don't "fix" it |
| Region | **East US 2** (`eastus2`) | All resources colocated here |
| Eligibility finding | **Claude-in-Foundry needs Enterprise/MCA-E** | An individual PAYG sub gets **0 TPM/RPM** for partner/Marketplace models. Directly-sold models (Azure OpenAI GPT, embeddings, DeepSeek, Grok) are PAYG-fine → **live chat model = Azure OpenAI GPT, not Claude** |

## 2. Resource inventory

| Resource | Type | Name | Endpoint | Tier / mode |
|---|---|---|---|---|
| **Foundry / AI Services** | Azure AI Services (multi-service) | `landdoc-rag-resource` | `https://landdoc-rag-resource.services.ai.azure.com` ‹confirm AOAI form — see §4› | Consumption |
| **Document Intelligence** | Azure AI Document Intelligence (ex–Form Recognizer) | `di-landdoc-hr01` | `https://di-landdoc-hr01.cognitiveservices.azure.com/` ‹confirm› | Consumption — **provisioned, not wired** (PdfPig path; OCR out of scope) |
| **Azure AI Search** | Cognitive Search (vector store, ADR-0017) | `srch-landdoc-hr01` | `https://srch-landdoc-hr01.search.windows.net` | **Free** tier (**eastus** — Free capacity was out in eastus2); index `landdoc-chunks`; **key auth (no MI)** |
| **Blob Storage** | Storage account | `stlanddochr01` | `https://stlanddochr01.blob.core.windows.net` | **LRS** (caught the GRS default) |
| ↳ container | Blob container | `documents` | — | — |
| **Key Vault** | Key Vault (RBAC) | `kv-landdoc-hr01` | `https://kv-landdoc-hr01.vault.azure.net` | RBAC auth; self = *Key Vault Secrets Officer* |
| **Budget** | Cost Management budget | `landdoc-budget` | — | **$25** @ 50 / 80 / 100 % alerts |
| **Auth app registration** | Entra app registration (Easy Auth — ADR-0022) | `landdoc-easyauth` (client id `8659ebef-c33b-4895-a228-dcb4838404c7`) | — | single-tenant; ID tokens on; secret `easyauth-aca` (2y) stored as ACA secret `microsoft-provider-authentication-secret` |

## 3. Model deployments (on `landdoc-rag-resource`)

| Purpose | Model | Deployment name | Protocol | Status |
|---|---|---|---|---|
| **Chat** | `gpt-5.4-mini` | `gpt-5.4-mini` (in `appsettings.json`) | **OpenAI Chat Completions / Responses** | **LIVE** — `AzureOpenAIChatClient` wired (PR #17) |
| **Embeddings** | `text-embedding-3-small` | `text-embedding-3-small` (in `appsettings.json`) | OpenAI embeddings | **LIVE** — `AzureOpenAIEmbeddingClient`, live slice default (PR #23) |

> **Protocol gotcha:** behind the Foundry gateway, **GPT speaks OpenAI Chat Completions**, Claude speaks the
> **Anthropic Messages** API. The catalog label tells you which adapter to write. So the live adapter is an
> **`AzureOpenAIChatClient` (Chat Completions)** — NOT the Anthropic-Messages `FoundryChatClient`.

## 4. Endpoint form — the #1 footgun ⚠️

The unified AI Services resource exposes `…services.ai.azure.com`, but the **`Azure.AI.OpenAI` SDK usually
wants the `https://landdoc-rag-resource.openai.azure.com/` form** for the AOAI data plane. Grab the exact
value from **Foundry → the resource → Keys & Endpoint** and store it as the KV secret below. Also pin an
**`api-version`** (use the `Azure.AI.OpenAI` ≥ 2.x default, or a current GA e.g. `2024-10-21`) ‹confirm›.

## 5. Key Vault secrets — names only (`--` in KV → `:` in .NET config)

| KV secret name | .NET config key | Holds |
|---|---|---|
| `AzureOpenAI--Endpoint` | `AzureOpenAI:Endpoint` | AOAI/Foundry endpoint (the `.openai.azure.com` form — see §4) |
| `AzureOpenAI--ApiKey` | `AzureOpenAI:ApiKey` | AOAI key 🔒 *(prefer managed identity in hosting; key for local/dev)* |
| `DocIntelligence--Endpoint` | `DocIntelligence:Endpoint` | Document Intelligence endpoint (provisioned, unused) |
| `DocIntelligence--ApiKey` | `DocIntelligence:ApiKey` | Document Intelligence key 🔒 (provisioned, unused) |
| `Search--Endpoint` | `Search:Endpoint` | Azure AI Search endpoint (`.search.windows.net`). Code binds the `Search` section (ADR-0017); supply via KV `Search--*` or env `Search__*` |
| `Search--ApiKey` | `Search:ApiKey` | Azure AI Search admin key 🔒 (Free tier = key auth, no MI — ADR-0017) |
| `Blob--ServiceUri` | `Blob:ServiceUri` | Blob endpoint (`https://stlanddochr01.blob.core.windows.net`) — the live MI / passwordless path (ADR-0018); chosen over an env var for consistency with the other endpoint secrets |
| `Blob--ConnectionString` | `Blob:ConnectionString` | Storage account connection string 🔒 (fallback when `Blob:ServiceUri`/MI not used) |
| `Anthropic--ApiKey` | `Anthropic:ApiKey` | Anthropic-direct fallback key 🔒 ‹confirm this 6th secret exists — build log says 6, provision note lists 5› |

Fetch a (non-secret) endpoint value:

```bash
az keyvault secret show --vault-name kv-landdoc-hr01 -n AzureOpenAI--Endpoint --query value -o tsv
```

**Hosting reads keys via managed identity** — grant the API's identity *Key Vault Secrets User* on
`kv-landdoc-hr01`; don't copy keys into app settings. **Local dev:** `dotnet user-secrets` (repo has a
`UserSecretsId` as of `175d1d7`).

## 6. Adapter wiring (→ builder / backend lane)

Wire behind the **existing ports**; keep local + Anthropic-direct as the **fallback** (config swap, not code change):

1. ✅ **Done (PR #17 / ADR-0012).** **`AzureOpenAIChatClient : IChatClient`** — `Azure.AI.OpenAI` / `Microsoft.Extensions.AI`,
   **Chat Completions**; client built in ctor from endpoint + key; default `ModelClient:ChatProvider` = `azureopenai`;
   `FoundryChatClient` Messages stub deleted.
2. ✅ **Done (PR #23 / ADR-0013).** **`AzureOpenAIEmbeddingClient : IEmbeddingClient`** — `text-embedding-3-small`,
   live slice default; `Embedding:Dimension` honored via the embeddings `dimensions` parameter; `LocalEmbeddingClient`
   demoted to offline/test (re-embed/re-upload on swap).
3. **Document Intelligence extractor** — replaces PdfPig field extraction.
4. ✅ **Done (spec 0006 / ADR-0018).** **`AzureBlobDocumentStore : IDocumentStore`** — persists original
   files + metadata in container `documents` (two blobs per doc: bytes + metadata JSON); managed-identity-
   preferred auth (`Blob:ServiceUri` + `DefaultAzureCredential`, connection-string fallback);
   `DocumentStore:Provider` switch (`azureblob` live / `inmemory` offline). Backs the document table +
   source-file viewer. **Role grant done:** the Container App's MI (`landdoc`) holds *Storage Blob Data
   Contributor* on `stlanddochr01` (the passwordless `ServiceUri` path); endpoint supplied via the
   `Blob--ServiceUri` Key Vault secret (§5).
5. ✅ **Built + Azure-wired (spec 0009 / ADR-0020).** **`AzureMonitorUsageSource : IUsageSource`** — reads
   **Azure Monitor platform metrics** (`MetricsQueryClient`, `Azure.Monitor.Query` 1.7.1) for the Foundry
   resource `landdoc-rag-resource` to back `GET /usage`; managed-identity auth (**no new secret**);
   `UsageSource:Provider` switch (`azuremonitor` live default in `appsettings.json` / `inmemory` offline-test).
   See the operator guide [USAGE-DASHBOARD.md](USAGE-DASHBOARD.md). Wired on **2026-06-09**:
   - **Role grant done:** the Container App's MI (`landdoc`, principal `<MI_PRINCIPAL_ID>`)
     holds **Monitoring Reader** on `landdoc-rag-resource` (`AIServices` — hosts the chat + embedding
     deployments; read-only, least privilege) — procedure in [DEPLOYMENT.md §1g](DEPLOYMENT.md).
   - **Config set (non-secret, NOT a Key Vault entry):** `Monitor__ResourceId` env var on the Container App =
     the `landdoc-rag-resource` resource id (persists across redeploys; the live adapter throws fast if
     unset). The `Pricing:<deployment>` table (`InputPer1K` / `OutputPer1K`, USD per 1K tokens) ships as
     committed **example** rates in `appsettings.json` — override via `Pricing__…` env vars for real dollars.
     Cost is computed (tokens × table), an estimate — Azure Cost Management is the future billing-grade cross-check.
   - **The `/usage` endpoint goes live when the feature ships to `main`** (CI/CD redeploys); the Azure wiring
     above is already in place.

**Priority:** chat + `/ask` FIRST (greens the floor on Azure-GPT) → embeddings → Doc Intelligence → Blob.
Record `AzureOpenAIChatClient` in **ADR-0012** (supersedes ADR-0007's Foundry-primary framing for the slice).

## 7. Phase D — hosted deploy (done)

Deployed as a **single container** (SPA + API on one origin, port 8080) on **Azure Container Apps**,
with secrets pulled from Key Vault via the app's managed identity — superseding the earlier Static Web
App + linked-backend plan ([ADR-0016](decisions/0016-single-container-azure-container-apps-keyvault-secrets.md)).
Operational steps live in [DEPLOYMENT.md](DEPLOYMENT.md) and [CICD.md](CICD.md).

| Component | Target | Name | Status |
|---|---|---|---|
| App (SPA + API, one container) | Azure **Container App** | `landdoc` (env `cae-landdoc`, eastus2) | **deployed** — https://landdoc.wittyground-3c06fff6.eastus2.azurecontainerapps.io/ |
| Image registry | Azure Container Registry (Basic) | `ca6a00db456cacr` | deployed |
| Secrets | Key Vault via app's system-assigned MI (`Key Vault Secrets User`) | `kv-landdoc-hr01` | deployed |
| Vector store (chunks) | Azure AI Search **Free tier** (index `landdoc-chunks`, **key auth**) | `srch-landdoc-hr01` (**eastus**) | wired (ADR-0017) |
| Document store (files + metadata) | Azure Blob via app's MI (`Storage Blob Data Contributor`), container `documents` | `stlanddochr01` | wired (spec 0006 / ADR-0018) |
| LLM usage metrics | Azure Monitor platform metrics via app's MI (`Monitoring Reader`) | `landdoc-rag-resource` | wired 2026-06-09 (spec 0009 / ADR-0020) — endpoint ships with the feature |
| Observability | Log Analytics (Container Apps env) | `workspace-rglanddocdeomoWNBf` | deployed |
| CI/CD | GitHub Actions → ACR build → ACA revision (OIDC, no stored secret) | `.github/workflows/deploy.yml` | armed (runs on merge to `main`) |
| Custom domain | ACA **custom domain** binding + free managed cert | **`landdoc.hectorreyes.dev`** — cert `mc-cae-landdoc-landdoc-hectorre-8517` (Namecheap CNAME + `asuid` TXT) | **bound** (SniEnabled, auto-renew) — https://landdoc.hectorreyes.dev/ |
| Single-user auth | ACA **built-in auth** (Easy Auth, Entra) + app allowlist middleware. Platform: redirect-to-login, `allowedPrincipals.identities=[96b6d850-0233-4865-a8aa-68249d3c675b]` (owner). App: env vars `Auth__Mode=easyauth`, `Auth__AllowedPrincipalIds__0=<owner oid>` | reg. `landdoc-easyauth` | **live** 2026-06-10 (spec 0013 / ADR-0022) — setup in [DEPLOYMENT.md §4](DEPLOYMENT.md) |
| App Insights | — | ‹optional› | not built |

## 8. Cost guardrails & teardown

- Model/storage resources are **consumption / no idle cost** (Default settings not PTU · LRS not GRS). Budget
  `landdoc-budget` $25 @ 50/80/100%. Phase D adds the Container App + ACR Basic; since 2026-06-10 the app
  is **scale-to-zero** (`min-replicas 0`), so idle cost ≈ ACR Basic alone (~$5/mo) at the price of a
  few-second cold start after idle. Pin back always-on:
  `az containerapp update -n landdoc -g rg-landdoc-deomo --min-replicas 1` (~$10–13/mo more).
- **Backlog — drop ACR for ghcr.io (idle → ~$0):** the registry (`ca6a00db456cacr`, Basic ~$5/mo flat)
  holds only the `landdoc` repo and is now the entire idle cost. Since the GitHub repo is public,
  images could ship to **GitHub Container Registry** (free for public images) instead: swap the
  registry login + image ref in `.github/workflows/deploy.yml`, point the Container App at
  `ghcr.io/hectorreyesdev/landdoc`, then delete the ACR. Needs a small spec when picked up
  (CI/CD seam — see [CICD.md](CICD.md)). Decided 2026-06-10, deliberately deferred.
- **Teardown after the interview (mandatory):**
  ```bash
  az group delete -n rg-landdoc-deomo --yes --no-wait        # drops every resource above (incl. Key Vault + AI)
  az ad app delete --id <CI_APP_ID> # the CI/CD Entra app — lives in Entra, not the RG
  az ad app delete --id 8659ebef-c33b-4895-a228-dcb4838404c7 # the Easy Auth app registration (ADR-0022)
  ```
  (Custom-domain CNAME at Namecheap is harmless to leave or remove.) For **targeted** teardown that keeps the
  Key Vault + AI resource, see [DEPLOYMENT.md §5](DEPLOYMENT.md).

## 9. Open confirmations before adapters go live

- [ ] Exact **AOAI endpoint form** (`.openai.azure.com` vs `.services.ai.azure.com`) + `api-version` (§4).
- [x] **Deployment names** for chat + embeddings — `gpt-5.4-mini` / `text-embedding-3-small` (in `appsettings.json`) (§3).
- [x] **Secret/config key names** — fixed by the code: `Search:Endpoint` / `Search:ApiKey` (KV `Search--*` or env `Search__*`), confirmed live (§5).
- [x] API host's **managed identity** granted *Key Vault Secrets User* on the vault — deployed (§7).
- [x] Record the **Azure AI Search service name + endpoint VALUE** (§2/§7) — `srch-landdoc-hr01` / `https://srch-landdoc-hr01.search.windows.net` (confirmed live 2026-06-09 via the eval run).
- [x] API host's **managed identity** granted *Storage Blob Data Contributor* on `stlanddochr01` (§6.4),
  and the blob endpoint supplied via the `Blob--ServiceUri` Key Vault secret (§5) — `DocumentStore:Provider`
  is the committed `appsettings.json` default (`azureblob`), so no env var is needed. (Local dev:
  `DocumentStore__Provider=inmemory`, or Azurite via `Blob__ConnectionString`.)
- [x] API host's **managed identity** (`landdoc`) granted *Monitoring Reader* on the Foundry resource
  `landdoc-rag-resource`, and `Monitor__ResourceId` set on the Container App (§6.5) — done 2026-06-09; no new
  secret. `UsageSource:Provider=azuremonitor` is the committed default; the `/usage` endpoint ships with the
  feature. (Local dev / CI: `UsageSource__Provider=inmemory`; see [USAGE-DASHBOARD.md](USAGE-DASHBOARD.md).)
