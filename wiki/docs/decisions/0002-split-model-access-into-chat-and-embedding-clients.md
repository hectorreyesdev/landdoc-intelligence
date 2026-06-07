# 0002. Split model access into IChatClient and IEmbeddingClient

- Status: Accepted
- Date: 2026-06-06

## Context
The first cut routed every model call (chat + embeddings) through one `IModelClient` with a
Foundry-primary / Anthropic-fallback pair. Two problems surfaced:
- **Anthropic has no embeddings endpoint** — an embeddings call cannot fall back to Anthropic, so a
  single interface implies a capability one provider can't honor.
- **Chat and embeddings fail over differently** and target different providers (chat: Foundry
  Claude/GPT → Anthropic direct; embeddings: local in-memory → Azure OpenAI). One interface hides that.

## Decision
We will split model access into two ports:
- **`IChatClient`** — chat/completions. Adapters `FoundryChatClient` (primary) and
  `AnthropicChatClient` (fallback). Selected by `ModelClient:ChatProvider`.
- **`IEmbeddingClient`** — embeddings only. Adapters `LocalEmbeddingClient` (slice default) and
  `FoundryEmbeddingClient` (Azure OpenAI `text-embedding-3-small`, production). Selected by
  `ModelClient:EmbeddingProvider`. There is **no** Anthropic embeddings adapter.

Provider selection stays config-only. Changing either interface requires a spec in `/specs`.

## Consequences
- Each concern fails over independently and honestly; no implied Anthropic-embeddings capability.
- Two small, focused interfaces instead of one broad one (marginally more surface, clearer contracts).
- `CLAUDE.md` and this wiki reflect the split; the interface-change guardrail covers both ports.
