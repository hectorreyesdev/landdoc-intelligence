# 0013. Azure OpenAI `text-embedding-3-small` as the live slice embedding adapter

- Status: Accepted
- Date: 2026-06-08
- Supersedes: [ADR-0008](0008-deterministic-hashing-embeddings-for-slice.md)

## Context
Embeddings sit behind the `IEmbeddingClient` port ([[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]).
[[knowledge/docs/decisions/0008-deterministic-hashing-embeddings-for-slice]] made `LocalEmbeddingClient`
— a deterministic FNV-1a bag-of-words hashing embedder — the **slice default**, naming Azure OpenAI
`text-embedding-3-small` as the out-of-scope production path. ADR-0008 itself flagged the escalation
trigger: *"if demo retrieval proves too weak, the next step is … the Foundry path — a new ADR if it
changes the slice default."* That trigger has now fired.

**Live testing at corpus scale broke retrieval *selection*, not just ranking polish.** Against ~36
sample land documents, targeted questions returned the wrong answer or none at all:
- *"Who is the lessee in Kern County, California?"* and *"… in Midland County, Texas?"* returned
  **"not found"** — the correct document never ranked into the top-k among many lexically-similar
  leases. The high-frequency shared vocabulary of the corpus (oil / gas / lease / county) dominates
  the hashed term-count vector and **drowns the few distinctive tokens** that actually identify a
  document; an unrelated probate order became a hash-collision **"hub"** that pulled rank away from
  the right lease.
- The failure is **not** a tuning problem. Chunk-size and top-k sweeps were ruled out empirically:
  raising top-k to 20 surfaced a **confident WRONG answer** drawn from a *different* Midland document
  rather than the intended one. The limit is the representation: lexical hashing encodes lexical
  overlap, **not meaning**, so the ranking it produces can't distinguish documents that share
  vocabulary but differ in the facts being asked about.

What is fixed and bounds the choice:
- **The slice is now a live demo by default.** [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]]
  already flipped chat to a live Azure OpenAI adapter (`gpt-5.4-mini`) on a PAYG subscription and
  established the posture *live by default, tests pinned offline*. Directly-sold Azure OpenAI models
  (GPT, **embeddings**) are PAYG-eligible — no Enterprise/MCA-E wall.
- **The Azure stack is provisioned.** `text-embedding-3-small` is deployed (Global Standard) on
  `landdoc-rag-resource` ([[knowledge/docs/AZURE-CONFIG]] §3).
- **The port and the cosine invariant are settled and unchanged.** ADR-0002 fixes `IEmbeddingClient`;
  ADR-0005 fixes the shared-dimension cosine invariant (every vector in a store shares one length).
  This decision changes the **adapter and its default**, not the interface.

Builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] and
[[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]; mirrors the
adapter+config posture of [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]];
relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]] and [[knowledge/docs/AZURE-CONFIG]].

## What carries forward (supersede is whole-file)
Marking ADR-0008 superseded closes that file; the truth in it that still holds is restated here so it
isn't silently lost:

- **`LocalEmbeddingClient` survives, demoted.** The deterministic FNV-1a hashing embedder is **not
  deleted** — it moves from *slice default* to the **offline / test embedder**: no network, no
  credentials, no cost, identical text → identical vector. It keeps `dotnet test` reproducible offline
  (the determinism that specs 0001/0002 require) and remains the fallback when Azure is unavailable.
- **The port contract is unchanged.** `IEmbeddingClient.EmbedAsync` is untouched; the **same adapter
  embeds the query at ask and the chunks at ingest**, so both live in one vector space.
- **The shared-dimension cosine invariant (ADR-0005) still holds** — all vectors in a store share one
  length, whichever adapter produced them.

## Decision
We will make **`AzureOpenAIEmbeddingClient` (`text-embedding-3-small`) the live slice default
embedder**, selected by config behind the unchanged `IEmbeddingClient` port. Specifically:

- Implement **`AzureOpenAIEmbeddingClient : IEmbeddingClient`** against Azure OpenAI embeddings
  (`Azure.AI.OpenAI` / `Microsoft.Extensions.AI`), serving the **`text-embedding-3-small`** deployment.
  Endpoint/key/deployment come from config (the per-provider `AzureOpenAI:*` section established by
  ADR-0012), never hardcoded; secrets via `dotnet user-secrets` (dev) or managed identity / Key Vault
  (hosting).
- **Honor `EmbeddingOptions.Dimension` via the embeddings `dimensions` request parameter** so the
  adapter emits vectors of exactly the configured length. `Dimension` stays the **single knob** that
  keeps the cosine invariant true across both adapters — the offline hashing embedder and the live
  Azure embedder produce same-length vectors for a given config.
- **Add a `ModelClient:EmbeddingProvider` switch arm** mirroring the chat switch from ADR-0012:
  `azureopenai` → `AzureOpenAIEmbeddingClient` (live), `local` → `LocalEmbeddingClient` (offline/test).
  *(assumption: the live arm is named `azureopenai`, consistent with ADR-0012's chat arm and retiring
  the older "foundry" label still in `ModelClientOptions`/RUNBOOK.)* Today the DI registration
  hardcodes `LocalEmbeddingClient`; this introduces the same config-selected pattern chat already uses.
- **Flip the committed default `ModelClient:EmbeddingProvider` from `local` to `azureopenai`** so the
  out-of-box demo retrieves semantically; the **test suite pins `EmbeddingProvider=local`** to stay
  offline and deterministic. *(assumption: appsettings default flips to live, exactly as ADR-0012 did
  for `ChatProvider` — same live-by-default / tests-pinned-offline posture.)*
- **Re-embed on switch.** Because the in-memory store ([[ADR-0005]]) holds vectors for the process
  lifetime, changing the embedder requires re-embedding the corpus — which here means simply
  **re-uploading the documents**; there is no persisted index to migrate.

This does **not** change the `IEmbeddingClient` interface (a port change would require a spec per
ADR-0002); it changes the adapter set, the DI selection, and config binding only. Binding on the
slice's embedding path.

## Consequences
- **Retrieval ranks by meaning.** The selection failures above resolve: `text-embedding-3-small`
  embeds semantics, so the right lease can outrank lexically-similar neighbours and county/lessee
  questions can land on the correct document.
- **A live cloud dependency and per-embed cost — on ingest *and* ask.** Every ingested chunk **and
  every question** now makes a billed embeddings call; `dotnet run` retrieval needs Azure credentials
  reachable (same posture chat already adopted in ADR-0012). Budget guardrails: [[knowledge/docs/AZURE-CONFIG]] §8.
- **Tests stay offline and deterministic** by pinning `EmbeddingProvider=local`; the demoted hashing
  embedder is exactly what keeps the suite reproducible with no network or cost.
- **Switching embedder means re-embedding the corpus**, not migrating a store — re-upload and the
  in-memory vectors are rebuilt under the new adapter. Cross-adapter vectors are never mixed.
- **`Dimension` is the one lever, and it is also a quality/cost lever.** `text-embedding-3-small`'s
  native width is 1536; the slice default `Dimension=256` requests a reduced embedding via the
  `dimensions` parameter — far stronger than hashing, but below native fidelity. Raising it trades
  memory (and marginally cost) for retrieval quality; it stays a single config knob, never a code
  change, and must match across the documents and queries in one store.
- **The port thesis holds again.** As with chat in ADR-0012, swapping the model behind
  `IEmbeddingClient` is a new adapter + config with no caller or interface change.
- **Given up:** the zero-dependency, fully-offline *default* retrieval path of ADR-0008. It is not
  lost — it is demoted to the test/offline default — but the out-of-box demo is no longer free or
  air-gapped.
- **Follow-on (code, via a spec + TDD):** implement `AzureOpenAIEmbeddingClient`; add the
  `EmbeddingProvider` switch arm and re-point the default in `appsettings.json`; pass `Dimension`
  through the `dimensions` parameter; resolve credentials via the existing `AzureOpenAI:*` section.
  Reconcile the embedding doc-comment on `IEmbeddingClient` (still names `FoundryEmbeddingClient`) to
  the merged code.
- **How-it-works doc propagation deferred to post-merge `/reconcile`** (the convention ADR-0012 set):
  STACK/ARCHITECTURE/DATA-FLOW/RUNBOOK rows that name `FoundryEmbeddingClient` / "deterministic
  hashing, slice default" are code-derived and reconcile to the merged adapter as reviewable diffs,
  not hand-edited ahead of it. The PRD scope note and the README ADR index are updated now.

## Links
- Supersedes [[knowledge/docs/decisions/0008-deterministic-hashing-embeddings-for-slice]].
- Builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] ·
  [[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]].
- Mirrors [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]].
- Relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]] · [[knowledge/docs/AZURE-CONFIG]].
