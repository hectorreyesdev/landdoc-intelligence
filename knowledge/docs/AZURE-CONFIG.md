# landdoc-intelligence — Azure / Foundry configuration handoff

> **What this is:** the single source of truth for the live Azure + Microsoft Foundry stack standing up
> behind [[landdoc-intelligence]]. Snapshot for the **builder session** (adapter wiring) and the **azure
> lane** deploy. Mentor-side state — lives in the vault; copy the relevant block into the builder as a prompt.
> **Status:** Phase B (stack provisioned) ✅ · Phase D (hosted deploy) in progress. **As of 2026-06-07.**
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
| **Document Intelligence** | Azure AI Document Intelligence (ex–Form Recognizer) | `di-landdoc-hr01` | `https://di-landdoc-hr01.cognitiveservices.azure.com/` ‹confirm› | Consumption |
| **Blob Storage** | Storage account | `stlanddochr01` | `https://stlanddochr01.blob.core.windows.net` | **LRS** (caught the GRS default) |
| ↳ container | Blob container | `documents` | — | — |
| **Key Vault** | Key Vault (RBAC) | `kv-landdoc-hr01` | `https://kv-landdoc-hr01.vault.azure.net` | RBAC auth; self = *Key Vault Secrets Officer* |
| **Budget** | Cost Management budget | `landdoc-budget` | — | **$25** @ 50 / 80 / 100 % alerts |

## 3. Model deployments (on `landdoc-rag-resource`)

| Purpose | Model | Deployment name | Protocol | Status |
|---|---|---|---|---|
| **Chat** | `gpt-5.4-mini` | ‹confirm — the name you gave the deployment; it's the SDK `deploymentName`› | **OpenAI Chat Completions / Responses** | **LIVE** — Playground reply confirmed 🎯 |
| **Embeddings** | `text-embedding-3-small` | ‹confirm deployment name› | OpenAI embeddings | Deployed (Global Standard) |

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
| `DocIntelligence--Endpoint` | `DocIntelligence:Endpoint` | Document Intelligence endpoint |
| `DocIntelligence--ApiKey` | `DocIntelligence:ApiKey` | Document Intelligence key 🔒 |
| `Blob--ConnectionString` | `Blob:ConnectionString` | Storage account connection string 🔒 |
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

1. **`AzureOpenAIChatClient : IChatClient`** — `Azure.AI.OpenAI` / `Microsoft.Extensions.AI`, **Chat Completions**;
   client built in ctor from endpoint + (managed-identity | key); flip default `ModelClient:ChatProvider` →
   `azureopenai`. **Floor-critical** — greens the live `/ask`. *(Delete the `FoundryChatClient` Messages stub.)*
2. **`AzureOpenAIEmbeddingClient : IEmbeddingClient`** — `text-embedding-3-small`; replaces the FNV-1a hashing
   placeholder (re-embed/re-upload docs after the swap).
3. **Document Intelligence extractor** — replaces PdfPig field extraction.
4. **Blob document store** — replaces local-disk uploads; container `documents`.

**Priority:** chat + `/ask` FIRST (greens the floor on Azure-GPT) → embeddings → Doc Intelligence → Blob.
Record `AzureOpenAIChatClient` in **ADR-0012** (supersedes ADR-0007's Foundry-primary framing for the slice).

## 7. Phase D — hosted deploy targets (in progress)

| Component | Target | Name | Custom domain | Status |
|---|---|---|---|---|
| Frontend (React SPA) | Azure **Static Web App** (Standard) | `swa-landdoc-hr01` ‹being created› | **`landdoc.hectorreyes.dev`** | pending step 3 |
| API (ASP.NET Core) | App Service **or** Container App ‹decide step 4› | ‹tbd› | via SWA **linked backend** → same-origin `/api/*` (no CORS) | pending step 4 |
| DNS | **Namecheap BasicDNS** (`*.registrar-servers.com`) | — | add `landdoc` CNAME in *Advanced DNS*; don't touch M365 records | — |
| Observability | App Insights | ‹tbd, optional› | — | step 6 |

## 8. Cost guardrails & teardown

- All resources are **consumption / no idle cost** (Default settings not PTU · LRS not GRS). Budget `landdoc-budget`
  $25 @ 50/80/100%. Phase D adds ~$9/mo SWA Standard + a small API host — still inside $25 for a few days.
- **Teardown after the interview (mandatory):**
  ```bash
  az group delete -n rg-landdoc-deomo --yes --no-wait
  ```
  One RG, one command — drops every resource above. (Custom-domain CNAME at Namecheap is harmless to leave or remove.)

## 9. Open confirmations before adapters go live

- [ ] Exact **AOAI endpoint form** (`.openai.azure.com` vs `.services.ai.azure.com`) + `api-version` (§4).
- [ ] **Deployment names** for chat + embeddings (the SDK `deploymentName`, not the model id) (§3).
- [ ] **Secret count/names** in `kv-landdoc-hr01` — confirm the Anthropic fallback secret (§5).
- [ ] API host's **managed identity** granted *Key Vault Secrets User* on the vault (§5).
