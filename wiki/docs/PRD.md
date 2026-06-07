# PRD — LandDoc Intelligence

## Problem
Land/title work buries answers in long, inconsistent PDFs — leases, title opinions, county
records. Finding "who owns the minerals", "what's the royalty", or "when does this lease expire"
means manually reading dozens of pages, and any answer must be traceable back to its source.

## Goals
- Ingest a land/title PDF and extract its key structured fields automatically.
- Answer free-text questions about an uploaded document with **citations** to the source chunk.
- Prove the full ingest → extract → embed → retrieve → answer loop end to end (vertical slice).
- Keep model access provider-swappable (Foundry primary, Anthropic fallback) by **config only**.

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
> TODO: fix the target extracted-field set and an acceptable retrieval-quality bar for the demo.

## Open questions
- Which exact fields make up the "extracted fields" view for the first document type?
- One document at a time, or a small corpus per session?
- Local embedding model for the slice — hashing-based, or a small ONNX model?
