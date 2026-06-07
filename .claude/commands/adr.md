---
description: Draft one complete Architecture Decision Record at wiki/docs/decisions/NNNN-<slug>.md (Nygard, auto-numbered) from the current conversation — clarifying anything ambiguous first, then asking to accept, and on acceptance flipping Status to Accepted and propagating the decision to affected docs.
argument-hint: "<decision title or slug> [— optionally reference a spec or prior ADR, e.g. 'switch to per-doc vector store (supersedes 0003)']"
---

# adr — clarify, draft, ratify, and propagate one Architecture Decision Record

Produce a single, **complete** ADR for a decision being made NOW, using everything available in the
current conversation plus the repo (CLAUDE.md, specs, manifests, prior ADRs). **First resolve any
genuine ambiguity with the human**; then fill every section — Context, Decision, Consequences —
making reasonable, clearly-flagged assumptions only on the small stuff. Then ask the human to accept
it. On acceptance, flip the Status to **Accepted** and update every document the decision touches.

> [!important] Reach common understanding first, then make the judgment auditable
> You DO author the Decision and Consequences here — that's the point. So you must actually
> understand the decision before writing it: **ask about anything material that's ambiguous,
> contradictory, or doesn't make sense (step 1) rather than guessing.** Once aligned, every
> committed line must survive the "explain it in a meeting" gate — ground claims in the
> conversation/repo, and reserve inline `*(assumption: …)*` for low-stakes defaults the human can
> catch at the accept gate. Never invent dates, versions, or names you could verify — check first.

## 0. Orient
- Project root = git repo root containing the cwd. Decisions live in `wiki/docs/decisions/`.
- Find the highest existing `NNNN-*.md` (ignore `0000-template.md`); next number = highest + 1,
  zero-padded to 4 digits. First real ADR is `0001`.
- Slug = `$ARGUMENTS` lowercased, kebab-cased, stopwords/punctuation dropped (e.g.
  "Switch embedding client to Azure OpenAI" → `0002-switch-embedding-client-to-azure-openai.md`).
- Gather the substance: re-read the current conversation for the decision, its drivers, and the
  tradeoffs already discussed. If `$ARGUMENTS` (or the discussion) references a spec (`specs/...`)
  or a prior ADR, read it — it's a Context pointer, and if this decision changes a prior
  **Accepted** ADR, it's a supersede (see step 6).
- **Verify, don't assume, what's cheap to check** — installed tool/runtime versions, file/symbol
  existence, dependency pins. Pull real facts into the draft.

## 1. Clarify — reach common understanding (before drafting)
- Before writing anything, weigh what you gathered in step 0 against `$ARGUMENTS` and surface
  **genuine** uncertainty: anything ambiguous, contradictory, that doesn't make sense to you, or
  that would materially change the Decision or its Consequences. Typical triggers: the decision
  overlaps or conflicts with a prior ADR; a key driver, constraint, or scope boundary is unstated;
  the title reads more than one way; a "fact" you'd otherwise assume can't be verified.
- **Ask, don't guess, on the things that matter.** Put the open questions to the human — use
  `AskUserQuestion` with concrete options when the choices are enumerable; ask in prose when it's
  open-ended or you need them to "explain further." Batch related questions; keep them short and
  specific.
- **Iterate** until you and the human share the same understanding of the decision, its scope, and
  its drivers. Only then move on to draft.
- **Don't over-ask.** Trivial, low-stakes defaults don't warrant a question — pick the reasonable
  one and flag it inline with `*(assumption: …)*` in the draft (step 3). The bar for asking is:
  *would getting this wrong change the decision, mislead a reader, or force a redraft?* If yes, ask.
- If nothing is genuinely unclear, say so in a line and proceed — don't manufacture questions.

## 2. Create the file (never clobber)
- Write `wiki/docs/decisions/NNNN-<slug>.md`. If a file with that number already exists, stop
  and report it — don't overwrite.

## 3. Draft the full ADR
```
# NNNN. <Title from $ARGUMENTS>

- Status: Proposed
- Date: <ISO yyyy-mm-dd, today>

## Context
<The forces at play, in plain terms: what's prompting this decision now, what's already fixed
(stack, the IChatClient / IEmbeddingClient seams, the in-memory cosine store, the out-of-scope
list), and the constraints/data that bound the choice. Pull specifics from the conversation and
repo (including anything settled during step 1). If a spec or prior ADR is related, add a pointer:
"Relates to [[specs/<NNNN-name>]]" / "Builds on [[wiki/docs/decisions/0001-record-architecture-decisions]]".
Mark any remaining gap-filling with *(assumption: …)*.>

## Decision
<The decision in active voice — "We will …". One tight paragraph. Be concrete: name the exact
versions / TFMs / interfaces / config keys involved, and state what the decision binds (e.g.
"binding on all projects in this repo"). This is drawn from the conversation and step-1 alignment;
where a minor detail is still open, choose the reasonable default and flag it *(assumption: …)*.>

## Consequences
<What becomes easier, harder, or newly possible — positive, negative, and neutral. Name the
tradeoffs accepted and what was given up, plus any follow-on work or risk. Bullet list is fine.>
```

## 4. Ask to accept (the gate)
- Present a short summary of the drafted Decision + the key Consequences and any flagged
  assumptions, then **ask the human whether to accept** (use `AskUserQuestion`: e.g. *Accept* /
  *Revise* / *Keep as Proposed*).
  - **Accept** → go to step 5.
  - **Revise** → apply the requested changes and ask again. Don't propagate until accepted.
  - **Keep as Proposed** → leave Status `Proposed`, skip steps 5–6, and report what's pending.

## 5. On acceptance — flip Status and propagate
- Set the ADR's `Status: Accepted` (keep the Date as the decision date).
- **Always add the new ADR to the `wiki/README.md` Decisions index** (the canonical list of every
  ADR). This is the one link required for every ADR regardless of topic — append it to the existing
  `[NNNN](docs/decisions/NNNN-<slug>.md)` series so the index never goes stale.
- **Propagate the decision to every doc it touches**, and link the ADR from each so it isn't an
  orphan. Use the repo's inline link convention from `wiki/docs/` pages:
  `see [ADR-NNNN](decisions/NNNN-<slug>.md)`. Common targets — update only what the decision
  actually changes:
  - **STACK.md** — runtime/lib/version rows and their "Why".
  - **ARCHITECTURE.md** — components/layers/diagrams; link where the affected piece is named.
  - **DATA-MODEL.md / DATA-FLOW.md** — entities/sequences the decision alters.
  - **API.md** — contract/endpoint/error-model changes.
  - **RUNBOOK.md** — install/run/build/env changes (e.g. a required SDK/tool version).
  - **GLOSSARY.md** — any new term the decision introduces.
- Keep edits tight and true to the code as it now stands; don't restate the whole ADR in each
  doc — state the resulting fact and link out.

## 6. Status & supersede discipline
- New ADRs start **Proposed**; step 4/5 is the only path to **Accepted**. Once Accepted an ADR is
  **immutable** — never edit its Decision/Consequences later.
- A changed decision is a *new* ADR that **supersedes** the old one: set the old ADR's Status to
  `Superseded by NNNN`, add a pointer both ways, and never rename/delete the old file.

## 7. Report
- Print the ADR path + number and its final Status. List every doc you updated (with the link
  added) and any supersede edits. If still Proposed, say exactly what's blocking acceptance and
  what propagation is deferred.
