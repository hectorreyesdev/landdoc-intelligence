# 0010. Anthropic-direct as the slice-default chat adapter

- Status: Superseded by [ADR-0012](0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md)
- Date: 2026-06-07

## Context
The read path ([[knowledge/docs/specs/0002-rag-qa-with-citations]]) needs a **working** real
`IChatClient` for the live-demo correctness check — the model must return the actual lessee name (and
admit when an answer isn't in the corpus), not just satisfy the offline fake. So one real chat adapter
has to be implemented and wired as the default for the slice.

Forces at play:
- **ADR-0007 sets the production posture, not the slice default.**
  [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]] makes the
  **Microsoft Foundry gateway the production primary** with Anthropic-direct as the availability
  **fallback**, selected by config (`ModelClient:ChatProvider`). That decision is about the production
  failover topology — it does not dictate which single adapter the vertical slice wires first.
- **Foundry provisioning is off the slice's critical path.** Standing up the Foundry gateway
  (resource, credentials, model deployment) is cloud-setup work that doesn't prove the RAG loop. The
  slice's job is to prove ingest → retrieve → grounded-cited-answer end to end with the least
  out-of-band setup.
- **Anthropic-direct is the shortest path to a real answer.** The default chat model is already
  `claude-opus-4-8` (an Anthropic model, per `CLAUDE.md`), and the Anthropic API is reachable with a
  single key in `dotnet user-secrets` — no gateway to provision.
- **The seam is config-only.** `IChatClient` is config-selected
  ([[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]); switching
  the default provider is a configuration value, not a code change, so this choice is reversible.

Relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]];
complements [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]];
builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] and
[[knowledge/docs/decisions/0004-modular-monolith-over-microservices]].

## Decision
We will make **Anthropic-direct the slice-default chat adapter**. `ModelClient:ChatProvider` defaults
to `anthropic`; `AnthropicChatClient.AnswerAsync` is implemented against the **official Anthropic .NET
SDK**, reading its API key from `ModelClient:ApiKey` (`dotnet user-secrets`, never committed) and its
model id from `ModelClient:Model` (default `claude-opus-4-8`). Base URL and credential are sourced from
config so a later swap to the Foundry adapter is **config-only**. `FoundryChatClient` remains a stub
for the slice.

This **does not supersede [[knowledge/docs/decisions/0007-microsoft-foundry-gateway-anthropic-direct-fallback]]**:
Foundry stays the intended **production primary**, and the Foundry→Anthropic availability failover
remains ADR-0007's concern (its own later spec). This ADR records a **slice-scoped default**, not a
change to the production topology — it references ADR-0007, and does not edit it.

## Consequences
- **A demoable answer with no gateway setup.** The live-demo check works against the real Anthropic
  API using a single secret — the RAG loop is proven without provisioning Foundry.
- **New dependency + a real secret.** Adds the official Anthropic .NET SDK package and introduces
  `ModelClient:ApiKey`, managed only via `dotnet user-secrets` / environment (never source,
  `appsettings.*`, or history — per `CLAUDE.md` guardrails).
- **Foundry path stays unexercised in the slice.** `FoundryChatClient` remains a stub; the production
  primary is not wired or tested here.
- **Production carry-forward is config-only.** Going to the ADR-0007 posture means re-pointing
  `ModelClient:ChatProvider` to `foundry`, implementing `FoundryChatClient`, and building the failover
  (ADR-0007's later spec) — no caller or port change, because the seam is unchanged.
- **ADR-0007 stays honest.** Recording this as a complementary, slice-scoped ADR — rather than editing
  0007 or silently defaulting against it — keeps the production decision intact and the divergence
  explicit.
