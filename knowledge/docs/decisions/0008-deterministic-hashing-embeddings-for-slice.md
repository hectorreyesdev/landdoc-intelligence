# 0008. Deterministic hashing embeddings in `LocalEmbeddingClient` for the slice

- Status: Accepted
- Date: 2026-06-06

## Context
Embeddings sit behind the `IEmbeddingClient` port. [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]]
established the port and named `LocalEmbeddingClient` as the **slice default** (no cloud) with
`FoundryEmbeddingClient` (Azure OpenAI `text-embedding-3-small`) as the production path — but it did
**not** record *how* `LocalEmbeddingClient` turns text into a vector. The ingest write path
([[knowledge/docs/specs/0001-document-ingestion-write-path]]) and the read path
([[knowledge/docs/specs/0002-rag-qa-with-citations]]) both need that answer now, and `PRD.md` carried
it as an open question: *"local embedding model for the slice — hashing-based, or a small ONNX model?"*

Forces at play:
- **Vertical slice, not production** — the job is to prove the `ingest → embed → store → retrieve`
  loop with the simplest thing that works; the demo corpus is tiny (a handful of leases), so
  embedding *quality* matters far less than the pipeline being exercised end to end.
- **Self-contained and free** — consistent with the slice's local-first stance and the in-memory
  store ([[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]),
  retrieval should carry no model download, no runtime dependency, no credentials, and no cost.
- **Deterministic tests** — specs 0001/0002 assert stable storage and **deterministic top-k**; the
  embedder must map identical text to an identical vector so `dotnet test` is reproducible offline.
- **Cosine needs equal-length vectors** — all vectors in the store must share one dimension
  (the shared-dimension invariant from ADR-0005 / `DATA-MODEL.md`).

Builds on [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]] and
[[knowledge/docs/decisions/0005-in-memory-vector-store-slice-azure-ai-search-production]]; relates to
[[knowledge/docs/specs/0001-document-ingestion-write-path]].

## Decision
We will implement `LocalEmbeddingClient` as a **deterministic hashing / bag-of-words embedder**: it
tokenizes text and hashes the tokens (term counts / hashed n-grams) into a **fixed-dimension
`float[]`**, then L2-normalizes the result, so identical input always yields an identical vector. The
dimension is a small constant read from config *(assumption: 256)*. The **same** `IEmbeddingClient`
embeds chunks at ingest and the query at ask, so both live in one vector space and share a dimension
(cosine invariant, ADR-0005). It requires **no model file, no download, no cloud call, and no
credentials**. The production path (`FoundryEmbeddingClient` → Azure OpenAI `text-embedding-3-small`)
is unchanged and out of scope. This is binding on the slice's `IEmbeddingClient` default and does
**not** change the `IEmbeddingClient` interface (an interface change would still require a spec per
ADR-0002).

## Consequences
- **Deterministic + offline.** `dotnet test` exercises ingest and retrieval reproducibly with no
  network; a fixed query yields a fixed top-k (the determinism specs 0001/0002 require).
- **Zero dependency / cost.** No ONNX runtime, no model artifact in the repo, no keys to manage.
- **Fast and trivial.** Embedding is cheap CPU work; nothing to warm up.
- **Tradeoff — crude semantics.** Hashing/bag-of-words captures lexical overlap, not meaning: no
  synonym or paraphrase matching. Fine for a tiny lease corpus with literal questions ("who is the
  lessee?"); weak when a question is worded unlike the source text.
- **Tradeoff — hash collisions.** A small dimension conflates distinct terms; raising the dimension
  trades memory for fidelity.
- **Tradeoff — slice↔prod is not a behavior-preserving swap.** Moving to `FoundryEmbeddingClient`
  changes vector semantics *and* dimension, so the store must be re-embedded and retrieval-quality
  expectations shift. The `IEmbeddingClient` seam keeps it a config/adapter change, but not a
  drop-in one.
- **Resolves** the PRD "local embedding model" open question toward hashing (not ONNX).
- **Follow-on / escalation.** Confirm the dimension; if demo retrieval proves too weak, the next step
  is a small local ONNX model or the Foundry path — a new ADR if it changes the slice default.
