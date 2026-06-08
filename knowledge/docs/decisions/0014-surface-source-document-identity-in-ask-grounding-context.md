# 0014. Surface source-document identity in the `/ask` grounding context

- Status: Accepted
- Date: 2026-06-08
- Refines: [ADR-0009](0009-corpus-wide-ask-retrieval-scope.md)

## Context
Retrieval is corpus-wide ([[knowledge/docs/decisions/0009-corpus-wide-ask-retrieval-scope]]): top-k
cosine across all chunks of all documents. The chat port receives each passage labeled only with an
opaque `[Chunk <guid>]` — and the store persists **no document filename** (a `Chunk` carries
`DocumentId` (GUID) + `Text`; there is no document registry). So when the corpus holds several documents
with **conflicting values** and the user asks a **document-qualified** question, the model cannot bind
passages to the named document and — correctly — **refuses rather than guess**.

Observed live (Playwright tour, 2026-06-08): single-document Q&A is flawless (lessee / lessor / royalty
all correct), but over a 3-lease corpus *"what royalty does the **Midland County** lease pay?"* returned
"The answer is not found" even though the chunk containing "royalty of one-fourth (1/4)" was cited
**#1**. This is exactly the cross-document-noise tradeoff ADR-0009 accepted and flagged as a follow-on.
Not a retrieval failure (the right chunk ranked first) and not a chat failure (single-doc works).

Relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]] (the read path; amended in lockstep with
this ADR) and builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]
(the `IChatClient` / `QaPassage` port seam).

## Decision
We will surface each passage's **source-document identity** (the ingested file name) to the grounding
prompt. Specifically: persist `Source` on `Chunk` at ingest; add `SourceName` to the `QaPassage` port
DTO (→ [[knowledge/docs/specs/0002-rag-qa-with-citations]] amendment —
`QaPassage(Guid ChunkId, Guid DocumentId, string SourceName, string Text)`); the `Qa` handler populates
it; and both chat adapters (`AzureOpenAIChatClient`, `AnthropicChatClient`) label each passage with its
source so the model can group passages by document and answer "which one." Retrieval scope is
**unchanged** (corpus-wide; ADR-0009 stands), and the public `/ask` request and `Citation` JSON response
contract is **unchanged**. This **refines ADR-0009 — it does not supersede it.**

## Consequences
- Document-qualified questions answer correctly over a multi-doc corpus; the honest "not found" on
  genuinely-absent facts (cite-or-nothing, spec 0002) is preserved.
- Small surface change: `Chunk` (+`Source`), the ingest path (set `Source` = file name), `QaPassage`
  (+`SourceName`) + the spec 0002 amendment, both chat adapters, and the affected tests.
- Carries forward to production: Azure AI Search would put document metadata in the prompt the same way.
- Follow-on (separate, frontend lane): also return the source name in `Citation` so the UI shows the
  file name instead of the GUID — its own contract change + spec touch.

## Links
- Refines [[knowledge/docs/decisions/0009-corpus-wide-ask-retrieval-scope]] (corpus-wide `/ask` scope —
  unchanged).
- Relates to [[knowledge/docs/specs/0002-rag-qa-with-citations]] (amended 2026-06-08) · builds on
  [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]].
