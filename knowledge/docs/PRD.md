# PRD — LandDoc Intelligence

## Problem
Land/title work buries answers in long, inconsistent PDFs — leases, title opinions, county
records. Finding "who owns the minerals", "what's the royalty", or "when does this lease expire"
means manually reading dozens of pages, and any answer must be traceable back to its source.

## Goals
- Ingest a land/title PDF and extract its key structured fields automatically.
- Answer free-text questions about an uploaded document with **citations** to the source chunk.
- Prove the full ingest → extract → embed → retrieve → answer loop end to end (vertical slice).
- Keep model access provider-swappable (Azure OpenAI GPT live, Anthropic-direct fallback — ADR-0012) by **config only**.

## Non-goals
"Production hardening" — explicitly out of scope (see `CLAUDE.md` → Out of scope):
VNet/Private Link · Azure AI Document Intelligence OCR tuning · Azure AI Search · auth/RBAC ·
observability stack. Also out: multi-tenant/multi-user concerns, durable persistence beyond the
process lifetime, and high-accuracy OCR of scanned/handwritten documents.

## Users / personas
- **Landman / title analyst** — uploads documents, reviews extracted fields, asks questions.
- **Engineer (this build)** — wires the slice and demonstrates the architecture.
> TODO: confirm the primary persona and the 2–3 questions they most need answered.

## User stories
- As an analyst, I upload a lease PDF and see its extracted fields (lessor, lessee, royalty, legal
  description, key dates) without reading the whole document.
- As an analyst, I ask "what is the royalty rate?" and get an answer that cites the exact chunk.
- As an analyst, I trust the answer because every claim links back to its source text.

## Scope
**In:** single-file (or small set) PDF upload · field extraction · chunk + embed · in-memory
retrieval · cited Q&A · React UI for upload / fields / ask.
**Out:** everything under Non-goals · durable storage · cloud vector search · auth.

## Success metrics
- End-to-end demo: upload → fields shown → question → cited answer, with no manual steps.
- Every answer carries at least one citation resolvable to a source chunk.
- Swapping `ModelClient:ChatProvider` between Foundry and Anthropic requires **no code change**.
> TODO: extracted-field set is fixed (lessor, lessee, legal description, royalty, key dates — spec
> 0001); still open: an acceptable retrieval-quality bar for the demo.

## Open questions
Resolved by the accepted slice specs:
- **Extracted-field set** — lessor, lessee, legal description, royalty, key dates
  ([spec 0001](specs/0001-document-ingestion-write-path.md)).
- **One document vs. corpus** — a global corpus query: `POST /ask` retrieves across all ingested
  documents ([spec 0002](specs/0002-rag-qa-with-citations.md),
  [ADR-0009](decisions/0009-corpus-wide-ask-retrieval-scope.md)).
- **Embedding model** — live slice default is Azure OpenAI `text-embedding-3-small`
  (`AzureOpenAIEmbeddingClient`), after the deterministic hashing embedder failed retrieval *selection*
  at corpus scale; the hashing embedder is demoted to the offline/test default
  ([spec 0001](specs/0001-document-ingestion-write-path.md),
  [ADR-0013](decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter.md),
  superseding [ADR-0008](decisions/0008-deterministic-hashing-embeddings-for-slice.md)).

Still open:
- Primary persona + the 2–3 questions they most need answered (see Users / personas).
- An acceptable retrieval-quality bar for the demo.
