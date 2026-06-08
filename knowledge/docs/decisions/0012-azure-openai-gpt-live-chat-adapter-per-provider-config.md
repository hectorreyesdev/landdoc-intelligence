# 0012. Azure OpenAI GPT as the live slice chat adapter; per-provider config schema

- Status: Accepted
- Date: 2026-06-08
- Supersedes: [ADR-0007](0007-microsoft-foundry-gateway-anthropic-direct-fallback.md), [ADR-0010](0010-anthropic-direct-slice-default-chat-adapter.md)

## Context
The read path ([[knowledge/docs/specs/0002-rag-qa-with-citations]]) is provider-agnostic behind the
`IChatClient` port ([[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]).
Two prior ADRs set the chat posture: [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]]
framed **Microsoft Foundry (Anthropic Messages) as production-primary** with Anthropic-direct as the
availability fallback; [[knowledge/docs/decisions/0010-anthropic-direct-slice-default-chat-adapter]]
made **Anthropic-direct the slice default** (`ModelClient:ChatProvider=anthropic`, a single shared
`ModelClient:ApiKey`, `FoundryChatClient` left as a stub).

Provisioning the live Azure stack (see [[knowledge/docs/AZURE-CONFIG]]) changed the facts on the
ground:

- **Claude-in-Foundry needs Enterprise/MCA-E.** An individual **PAYG** subscription gets **0 TPM/RPM**
  for partner/Marketplace models (Claude is sold via the Marketplace), so the Foundry-serves-Claude
  primary that ADR-0007 assumed is not reachable on the live subscription. Directly-sold Azure OpenAI
  models (GPT, embeddings) **are** PAYG-eligible.
- **The reachable live chat model is Azure OpenAI `gpt-5.4-mini`**, which speaks **OpenAI Chat
  Completions** — a different wire protocol from Anthropic Messages. A different protocol means a
  different adapter (`AzureOpenAIChatClient`), and the `FoundryChatClient` Anthropic-Messages stub no
  longer fits.
- **One shared key can't run two providers.** Today's `ModelClientOptions` packs a single `ApiKey`
  (and `Model`) into one `ModelClient` section. Running Azure-GPT live **with** Anthropic-direct as a
  config-swap fallback — and demonstrating a config-only provider swap — needs both providers'
  credentials resolvable at once. The Key Vault secrets are already named per-provider
  (`AzureOpenAI--Endpoint`/`AzureOpenAI--ApiKey`, `Anthropic--ApiKey` → `AzureOpenAI:Endpoint`/
  `AzureOpenAI:ApiKey`, `Anthropic:ApiKey`), which implies a per-provider config schema.

This ADR is **chat-scoped**. It builds on ADR-0002 (the port split is unchanged) and does not touch
ADR-0008 (hashing embeddings remain the slice embedding path — a separate decision when the embedding
adapter lands). It supersedes ADR-0007 and ADR-0010 (next section).

## What carries forward (supersede is whole-file)
Marking 0007 and 0010 superseded closes those files; the truth in them that still holds must be
restated here so it isn't silently lost:

- **From ADR-0007 — the gateway / port-swappability thesis stands, and this change validates it.**
  Model access stays behind `IChatClient`; provider + model are selected by config; **only the model
  behind the port changed** (Foundry Anthropic-Messages → Azure OpenAI Chat Completions). The gateway
  pattern is **not** abandoned — Azure OpenAI is reached through the same Azure AI Services / Foundry
  resource (`landdoc-rag-resource`).
- **From ADR-0010 — Anthropic-direct survives, demoted.** `AnthropicChatClient` (official Anthropic
  .NET SDK) remains wired and selectable; it moves from **slice-default** to **config-swap fallback**
  (`ChatProvider=anthropic` still works). Its key handling (user-secrets, never committed) is
  preserved under the new per-provider section.

## Decision
We will make **Azure OpenAI GPT the live slice chat provider**. Specifically:

- Implement **`AzureOpenAIChatClient : IChatClient`** against the **OpenAI Chat Completions** protocol
  (`Azure.AI.OpenAI` / `Microsoft.Extensions.AI`), serving **`gpt-5.4-mini`** — the deployment/model
  id comes from config, never hardcoded (CLAUDE.md).
- Flip the default **`ModelClient:ChatProvider` from `anthropic` to `azureopenai`**.
- Adopt a **per-provider config schema**: move credentials/endpoints out of the single shared
  `ModelClient:ApiKey` into per-provider sections — `AzureOpenAI:{Endpoint, ApiKey, Deployment,
  ApiVersion}` and `Anthropic:{ApiKey, Model}` — so both providers resolve simultaneously and a swap
  is config-only. Secrets come from `dotnet user-secrets` (dev) or **managed identity** / Key Vault
  (hosting), never committed. *(assumption: `ChatProvider` stays the top-level selector under
  `ModelClient`; exact key names track the KV secret names in [[knowledge/docs/AZURE-CONFIG]] §5.)*
- **Delete `FoundryChatClient`** (the Anthropic-Messages stub) and its `foundry` switch arm.
  Claude-in-Foundry is gated on Enterprise/MCA-E and stays **out of slice scope**.
- **Keep `AnthropicChatClient`** as the config-swap fallback (`ChatProvider=anthropic`).

This does **not** change the `IChatClient` interface (a port change would require a spec per ADR-0002);
it changes adapters and config binding only. Binding on the slice's chat path.

## Consequences
- **PAYG-eligible live demo.** `/ask` runs against a real chat model on an individual PAYG
  subscription — no Enterprise/MCA-E, no Marketplace quota wall.
- **The port thesis is proven, not just claimed.** Swapping the model behind `IChatClient` was a new
  adapter + config, with no caller or port change — exactly what ADR-0002/0007 promised.
- **Two live adapters, both credentialed.** Per-provider config lets Azure-GPT and Anthropic-direct
  coexist, so a provider swap is a config edit and is demoable.
- **Protocol divergence is explicit.** Azure GPT = Chat Completions; Anthropic = Messages. Cross-
  provider answer **parity isn't guaranteed** across a swap (different model families by default) —
  the parity caveat ADR-0007 flagged, now sharper.
- **Given up:** the recorded "Foundry-serves-Claude primary" posture and the Foundry-Messages stub. If
  Claude-via-Azure becomes reachable later, that's a new ADR once eligibility allows.
- **Cost levers intact.** Per-call-type model tiering and prompt caching stay available because
  model/deployment ids live in config; Azure budget guardrails are in [[knowledge/docs/AZURE-CONFIG]] §8.
- **Follow-on (code, via a spec + TDD):** implement `AzureOpenAIChatClient`; refactor
  `ModelClientOptions` → per-provider options; delete `FoundryChatClient` + the switch arm; re-point
  the default; resolve credentials via managed identity/key. The wiring plan and go-live blockers
  (endpoint form, deployment names, api-version) are [[knowledge/docs/AZURE-CONFIG]] §6 and §9.
- **Doc propagation deferred to post-merge `/reconcile`.** STACK rows (chat models / Anthropic SDK)
  and any ARCHITECTURE/GLOSSARY mentions of "Foundry primary" / "Anthropic slice default" are
  how-it-works docs — they reconcile to the merged code as reviewable diffs, not hand-edited ahead of it.

## Links
- Supersedes [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]] ·
  [[knowledge/docs/decisions/0010-anthropic-direct-slice-default-chat-adapter]].
- Builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]].
- Relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]] · [[knowledge/docs/AZURE-CONFIG]].
