---
description: Draft one complete feature spec at specs/NNNN-<slug>.md — auto-numbered, clarifying anything ambiguous first, then asking to accept; on acceptance flips Status to Accepted. The spec-first pillar — commit the spec BEFORE the implementation.
argument-hint: "<short-slug-for-the-feature> (e.g. pdf-field-extraction)"
---

# spec — clarify, draft, and ratify a feature spec (spec-first)

Produce ONE complete feature spec for `$ARGUMENTS`, using the current conversation plus the repo
(CLAUDE.md, wiki/docs, prior specs, ADRs). **First resolve any genuine ambiguity with the human**;
then fill every section — What to build, Constraints, How to verify, Links — making reasonable,
clearly-flagged assumptions only on the small stuff. Then ask the human to accept it. On acceptance,
flip the Status to **Accepted**. The spec is the spec-first pillar: once accepted it is committed on
its own **before** any implementation.

> [!important] Reach common understanding first, then make the judgment auditable
> A spec is judgment — what to build, its scope, and its acceptance. So you must actually understand
> the feature before writing it: **ask about anything material that's ambiguous, contradictory, or
> doesn't make sense (step 1) rather than guessing.** Once aligned, every committed line must survive
> the "explain it in a meeting" gate — ground claims in the conversation/repo, and reserve inline
> `*(assumption: …)*` for low-stakes defaults the human can catch at the accept gate. Never invent
> scope, constraints, or acceptance you could confirm — ask or check first.

## 0. Orient
- Project root = the git repo root containing the cwd. Work there. Specs live in `specs/`
  (root-relative, not `/specs`).
- Scan `specs/` for the highest existing `NNNN-` prefix (numeric-prefixed files only — ignore
  `README.md` and anything without a 4-digit prefix). New number = highest + 1, zero-padded to 4
  digits (first spec = `0001`). If `specs/` is missing, create it.
- Slug = `$ARGUMENTS`, lowercased, spaces→hyphens, stripped of punctuation. File =
  `specs/<NNNN>-<slug>.md`. If a file with that slug already exists, STOP and report it — don't
  clobber or renumber.
- Gather the substance: re-read the conversation for the feature, its intent, and its boundaries.
  Read the stack/docs it touches and any related spec or ADR (it may depend on, or need, a decision
  recorded via `/adr`). **Verify what's cheap to check** — module names, interfaces, file existence.

## 1. Clarify — reach common understanding (before drafting)
- Before writing anything, weigh what you gathered in step 0 against `$ARGUMENTS` and surface
  **genuine** uncertainty: anything ambiguous, contradictory, that doesn't make sense to you, or
  that would materially change what gets built or how it's accepted. Typical triggers: the feature's
  scope or in/out boundary is unstated; the user- or demo-facing capability is unclear; which
  module(s) (`Ingestion` / `Extraction` / `Retrieval` / `Qa`) or interfaces it touches is open; it
  depends on something not yet built or needs an architecture decision (an ADR) first; the acceptance
  bar ("done means…") isn't obvious.
- **Ask, don't guess, on the things that matter.** Put the open questions to the human — use
  `AskUserQuestion` with concrete options when the choices are enumerable; ask in prose when it's
  open-ended or you need them to "explain further." Batch related questions; keep them short and
  specific.
- **Iterate** until you and the human share the same understanding of the feature, its scope, and its
  acceptance. Only then move on to draft.
- **Don't over-ask.** Trivial, low-stakes defaults don't warrant a question — pick the reasonable one
  and flag it inline with `*(assumption: …)*` in the draft (step 2). The bar for asking is: *would
  getting this wrong change what's built, mislead a reader, or force a redraft?* If yes, ask.
- If nothing is genuinely unclear, say so in a line and proceed — don't manufacture questions.

## 2. Write the full spec
```
# <NNNN> — <Title from slug, Title Case>

**Status:** Draft        <!-- Draft → Accepted; flipped on acceptance (step 4) -->

## What to build
<2–3 short paragraphs in plain language: what this feature is and the user- or demo-facing
capability it adds to the RAG slice. State intent, not implementation. Draw from the conversation
and the step-1 alignment.>

## Constraints
<What bounds this. Pull from the stack where it's a fact (.NET 10 Web API under /backend, React/TS
SPA under /frontend, `IChatClient` / `IEmbeddingClient`, in-memory cosine store). Call out anything
explicitly OUT of scope for this feature (e.g. NOT Azure AI Search, NOT Azure AI Document
Intelligence OCR tuning, NOT auth/RBAC) so the boundary is visible. Flag minor open defaults with
*(assumption: …)*.>

## How to verify
<Concrete acceptance checks — what must be observably true for this to be done. Prefer checkable
statements (a request returns citations; a field is extracted from the sample PDF; top-k retrieval
is deterministic for a fixed query). These become the PR's "verify" gate.>

## Links
<Related ADR ([[wiki/docs/decisions/NNNN-<title>]]), affected docs (ARCHITECTURE / DATA-MODEL /
DATA-FLOW / API), and the implementing PR once it exists (leave a placeholder until known).>
```

## 3. Ask to accept (the gate)
- Present a short summary of What-to-build + the key Constraints and acceptance checks, plus any
  flagged assumptions, then **ask the human whether to accept** (use `AskUserQuestion`: e.g.
  *Accept* / *Revise* / *Keep as Draft*).
  - **Accept** → go to step 4.
  - **Revise** → apply the requested changes and ask again. Don't flip Status until accepted.
  - **Keep as Draft** → leave Status `Draft`, skip step 4, and report what's pending.

## 4. On acceptance — flip Status + commit spec-first
- Set the spec's **Status** to `Accepted`.
- **Spec-first commit discipline (the whole point):** the spec is committed **on its own, BEFORE any
  implementation commit.** The commit ordering is the visible spec-first evidence the reviewer and
  the PR template check — spec commit first, code commit(s) after, referencing this spec. Offer to
  commit the spec now with a clean message (no `Co-Authored-By` / "Generated" trailer); if the user
  defers, remind them it must land before they touch implementation.

## 5. Report
- Print the spec path + number and its final Status. If Accepted, state whether the spec-first commit
  was made or is pending. If still Draft, say exactly what's blocking acceptance.
