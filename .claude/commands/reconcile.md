---
description: Close the doc↔code drift loop. Clarifies ambiguous drift first; for each item I declare the source of truth; then it regenerates derived docs as reviewable diffs, drafts judgment edits in full for me to accept or revise, supersedes ADRs (never edits them), or proposes the conforming code change. Never decides direction; never silently commits.
argument-hint: "[optional doc or area to scope, e.g. ARCHITECTURE or api]"
---

# reconcile — resolve doc↔code drift (I pick the direction)

The companion to `/wrap`. `/wrap` only **flags** drift; `/reconcile` **closes** it — but it never decides
which side is right. For every drift item, **I** declare the source of truth; the command does only the
mechanical execution and always leaves judgment to me. This human-direction gate is the whole point: in a
public repo committed under my name, an agent must never silently overwrite a design decision.

> [!important] The one rule that governs everything — I pick the direction, per item
> Propose nothing until I have declared, for each drift item, whether the CODE is canonical (update the
> doc) or the DOC is canonical (change the code). **No global "reconcile all / accept all," no unattended
> run, no inferring the direction yourself.** An item with no decision stays flagged. The clarifying
> questions (step 1) and the accept/revise gate (step 3) exist so I *understand and ratify* — they never
> choose the direction or resolve an item for me.

> [!important] What it may write vs. only propose
> **Regenerate as a reviewable diff (I stage):** *derived* doc content only — versions, ERD field lists,
> endpoint shapes, diagrams that mirror module structure.
> **Draft in full, then accept/revise (never auto-commit):** *judgment* content — PRD goals/scope,
> architecture rationale. The command writes the complete proposed wording and asks me to accept or
> revise; on accept it applies the ratified text as a staged diff — it does not leave an unfinished
> author-flag behind, and it does not commit.
> **Never edit:** an **Accepted ADR** — it is immutable; reconcile a changed decision by authoring a NEW
> superseding ADR via `/adr`.
> **Propose, never self-review or auto-commit:** code changes — they go through the normal reviewer + CI + human gate.

## 0. Orient + detect drift
- Project root = git repo root containing the cwd. If `wiki/` is missing, run `/wiki-init` first.
- **If there's no app code yet, STOP and say so** — pre-scaffold, the docs ARE the source of truth (design
  intent); there is nothing to reconcile.
- Run the same drift scan as `/wrap` step 4: diff each `wiki/docs/*` claim (and the ADRs) against the
  current code / manifests. Scope to `$ARGUMENTS` if given. Emit a **numbered drift list**, each item naming
  the **doc + section** and the **conflicting code fact**. If there's no drift, say so and stop.

## 1. Clarify the drift list (before asking for directions)
- Review the numbered list and surface **genuine** uncertainty before I start choosing directions: an item
  you're not sure is real drift (vs. a misread of the code or the doc); an item whose conflicting fact is
  ambiguous or could be read more than one way; an item where the *right way to resolve it* isn't obvious
  and would change what you'd propose; anything that simply doesn't make sense to you.
- **Ask, don't guess, on the things that matter.** Put the open questions to me — use `AskUserQuestion`
  with concrete options when enumerable; ask in prose when it's open-ended or you need me to "explain
  further." Batch related questions; keep them short and specific.
- **Iterate** until the drift list is mutually understood. This step is about *understanding the items* —
  it is NOT where direction is chosen and it never resolves an item on its own.
- **Don't over-ask.** If an item is clear, don't manufacture a question for it. The bar is: *would
  misunderstanding this change what you propose, or which direction makes sense?* If yes, ask.
- If the whole list is unambiguous, say so in a line and proceed.

## 2. For EACH drift item, make me choose the source of truth
Present the item, then STOP for my decision — exactly one of:
- **`code`** — code is right, doc is stale → update the doc.
- **`doc`** — doc is right, code drifted → change the code.
- **`skip`** — leave it flagged; I'll handle it later.
Do not proceed on an item until I've chosen. Never infer the direction.

## 3. Resolve per my choice

### `code` is canonical → update the doc
- **Derived content** (versions, ERD fields, endpoint shapes, structure diagrams): regenerate that section
  from the current code and show it as a **diff**. Do NOT stage or commit — I review and stage it.
- **Judgment content** (PRD goals/scope, architecture rationale, the "why" columns): draft the **full
  proposed rewrite** — grounded in the code/conversation, with any minor open default flagged inline as
  `*(assumption: …)*` — then **ask me to accept or revise** (`AskUserQuestion`: *Accept* / *Revise*).
  - **Accept** → apply the ratified wording to the doc as a **staged diff**; never commit it.
  - **Revise** → adjust per my feedback and ask again. Do not apply until accepted.
  This replaces the old "leave an author-flag for me to write" step: you write it in full, I ratify.
- **An ADR**: do NOT touch its body. Accepted ADRs are immutable. Author a **new superseding ADR** via the
  `/adr` flow (which clarifies + I accept); then set the old ADR's **Status** line to `Superseded by
  NNNN` with a pointer both ways. (Editing only that Status line is allowed; the old Context/Decision/
  Consequences stay frozen.)

### `doc` is canonical → change the code (implement-to-spec)
- Treat the doc as the spec. Summarize the exact code change needed to conform, then propose it as a **diff**.
- This is a CODE change and follows the normal path: if it's non-trivial, open a `/spec` first; the
  fresh-context reviewer + CI + human gate vet it before merge. `/reconcile` does **not** self-review its
  own code change and does **not** auto-commit it — hand it to the Writer/Reviewer flow.

## 4. Never cross these lines
- No item resolved without my explicit `code` / `doc` / `skip` — clarifying questions never substitute for it.
- No Accepted ADR body edited (supersede instead).
- No judgment-doc edit applied without my accept, and nothing committed on my behalf; accepted judgment
  edits and mechanical doc regens are diffs I stage.
- Env-var **names only** in any doc — never values.

## 5. Report + hand off to /wrap
- Summarize per item: the direction I chose, what was regenerated / accepted / proposed, and what still
  needs my review or is still flagged (skipped/Revise-pending). List any new files (e.g. a superseding ADR).
- Remind me to run `/wrap` afterward to log the reconciliation (decisions; a `lessons.md` line if a real
  correction occurred) and to re-run the drift check so the log reflects a clean state.
