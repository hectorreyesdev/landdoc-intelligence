# 0007. Microsoft Foundry gateway (primary) + Anthropic-direct fallback for chat

- Status: Superseded by [ADR-0012](0012-azure-openai-gpt-live-chat-adapter-per-provider-config.md)
- Date: 2026-06-06

## Context
Chat/completions sit behind the `IChatClient` port. The port split itself — and the *existence* of a
`FoundryChatClient` (primary) / `AnthropicChatClient` (fallback) pair — was decided in
[[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]. What 0002 did **not**
record is *why* this provider topology, or *how* failover behaves: ARCHITECTURE.md still carries an
open `TODO` to "define the failover trigger (which exceptions / status codes flip primary →
fallback)." This ADR records the chat-provider gateway choice and that failover policy. It **builds
on, and does not supersede, 0002.**

Forces at play:
- **Azure is the production cloud.** Microsoft Foundry is the Azure-aligned model gateway, so the
  primary chat path matches where production runs *(assumption: Antero standardizes on Foundry as its
  model gateway)*.
- **One endpoint, swappable model.** A gateway can front a **Claude or a GPT** model behind a single
  endpoint, so changing the served model is configuration, not code — consistent with 0002's
  config-only selection (`ModelClient:ChatProvider`, model IDs in config; never hardcoded).
- **A single provider is a single point of failure.** The default chat model is `claude-opus-4-8`
  (CLAUDE.md); calling Anthropic's API directly is a same-family (Claude) fallback that keeps chat
  answering when the gateway is unavailable.
- **Cost levers depend on config.** Per-call-type model tiering (Sonnet 4.6 / Haiku 4.5 for cheaper
  steps like extraction) and prompt caching for the repeated document context are only possible
  because model IDs live in config.

Relates to [[knowledge/docs/decisions/0003-dotnet-10-lts]] (the Azure-targeted backend). Embeddings are
out of scope here — Anthropic has no embeddings endpoint (0002).

## Decision
We will make **Microsoft Foundry the primary chat provider** behind `IChatClient`
(`FoundryChatClient`) and **Anthropic's API direct the fallback** (`AnthropicChatClient`, via the
`Anthropic` NuGet SDK). Foundry is the gateway because it fronts either a Claude or a GPT model
behind one Azure-aligned endpoint, so switching the served model is config, not code
(`ModelClient:ChatProvider` + model IDs in config, per 0002); the default is `claude-opus-4-8`, with
cheaper tiers selectable per call-type and prompt caching for repeated document context. On a
**Foundry availability failure — connection errors, timeouts, HTTP 5xx, or 429 after backoff —
`IChatClient` falls back to Anthropic-direct**; request-level `4xx` (400/401/403) do **not** trigger
fallback, since they would fail identically downstream and signal a bug/config error, not an outage
*(assumption: this exact trigger set and the retry/backoff before flip are not yet pinned in code)*.
This is binding for the slice's chat path; it does not change the `IChatClient` interface (an
interface change would still require a spec per 0002).

## Consequences
- **Model/vendor flexibility.** Claude↔GPT or a model-ID change is a config edit, not a code change.
- **Resilience.** Chat survives a Foundry outage via the Anthropic-direct fallback on the same Claude
  family; the failover trigger is now defined (closing the ARCHITECTURE `TODO`).
- **Cost control stays open.** Per-call-type tiering + prompt caching remain available because model
  selection is config-driven.
- **Production alignment.** The primary path mirrors the production cloud (Foundry/Azure), so slice
  and prod share the gateway shape.
- **Tradeoff — two integrations.** A Foundry adapter *and* an Anthropic SDK adapter, plus two sets of
  credentials; the fallback path needs its own tests or it silently rots.
- **Tradeoff — cross-provider parity.** If Foundry is configured to serve **GPT** while the fallback
  is **Claude**, answers can differ across a failover *(assumption: in the slice Foundry serves
  Claude, so parity holds — revisit if Foundry is pointed at GPT)*.
- **Follow-on:** pin the `Anthropic` NuGet version (STACK `TODO`), implement the failover wrapper, and
  finalize the retry/backoff thresholds before fallback flips.
