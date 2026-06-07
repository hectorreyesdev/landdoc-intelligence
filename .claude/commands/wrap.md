---
description: Wrap a build session — clarify any ambiguous curation calls, append to knowledge/logs/, capture knowledge/notes/, log corrections to knowledge/lessons.md, drift-check (never edit) the living knowledge/docs/, flag missing judgment artifacts, then confirm before committing only what I authored.
argument-hint: "[optional session topic]"
---

# wrap — end-of-build-session logging + living-docs drift check (public repo)

Curate, don't transcribe. Be concise. This repo is **public and committed under my name**, so
the iron rule is **author-vs-enforce**: you may author narrative/append-only artifacts (the log,
lessons, notes) and you may **flag** drift and missing judgment — but you must **not** decide,
rewrite, or commit the how-the-app-works design docs. Those encode boundary decisions I have to
defend in an interview; a slash-command edit there lands committed under my name. Do these steps
in order.

> [!important] Routing rule — where content lives (split by KIND)
> **`knowledge/logs/` = what happened · `knowledge/docs/` = how the app works (living) · `knowledge/notes/` = what I know · `knowledge/lessons.md` = what was learned + the rule.**
> Don't duplicate across them: a session narrative goes in the log; durable how-the-system-works
> truth lives in docs (and **I** edit those); reusable cross-cutting knowledge goes in notes.

> [!important] What /wrap may write vs. only flag
> **Authors + commits:** `knowledge/logs/`, `knowledge/notes/`, `knowledge/lessons.md` — narrative, append-only, low-judgment.
> **NEVER writes/commits/regenerates:** `knowledge/docs/*` (PRD · STACK · ARCHITECTURE · DATA-MODEL · DATA-FLOW · API · RUNBOOK · GLOSSARY) **and** `knowledge/docs/decisions/` (ADRs) and their Mermaid. For these, /wrap **drift-detects and flags only** — the human edits and stages them.

## 0. Orient
- Project root = the git repo root containing the cwd. Work there. **If `knowledge/` doesn't
  exist yet, run `/kb-init` first** (this command depends on that tree).
- Read the latest `knowledge/logs/<date>.md`, `knowledge/lessons.md`, and `git log` / `git diff` since the
  last wrap (or the last few commits) to see what actually changed this session. Note the spec,
  ADR, and PR/branch this work maps to — you'll link them as evidence in step 2.

## 1. Clarify ambiguous curation calls (only when material)
- /wrap curates "what happened" — most of it is factual and derivable from git + the session. But a
  few calls are genuine judgment: is a finding **durable/reusable** enough for a note, or does it
  stay in the log? does a decision rise to a **significant decision that needs an ADR**? how should
  the session be segmented into topics? (Lessons have their own propose-then-ask gate in step 4.)
- When such a call is **genuinely ambiguous AND would change what gets written**, ask me before
  guessing — `AskUserQuestion` with concrete options when enumerable, prose when open-ended. Batch
  related questions; keep them short.
- **Don't over-ask.** Wrapping up should stay fast — if a call is clear, make it and move on. The bar
  is: *would getting this wrong put a wrong or missing entry in the log, notes, or lessons?* If yes,
  ask; otherwise proceed.
- This step never authors `knowledge/docs`/ADRs — clarifying a drift or a missing artifact still ends in a
  **flag** (steps 5–6), not an edit.

## 2. Log → `knowledge/logs/<YYYY-MM-DD>.md` (ADDITIVELY — never duplicate)
- Target today's file; create it with a `# <YYYY-MM-DD>` H1 + YAML frontmatter (`date`,
  `tags: []`) if missing. **READ it first** and append only the delta since the last entry.
- Same work session as the latest `##` → add new bullets under its `###` headers. A new
  topic → start a new `## <topic>` section (use `$ARGUMENTS` as the topic if given). Nothing
  meaningful changed → add nothing and say so. Structure (keep it grep-searchable):

```
## <topic>
**TL;DR:** <one line>
Tags: #tag1 #tag2 · Notes touched: [[note-a]] · [[note-b]]
Evidence: [[knowledge/docs/specs/<NNNN-feature>]] · [[knowledge/docs/decisions/NNNN-<title>]] · PR #<n> / <branch>

### Decisions
- **Decision:** <what + the why, one line>

### Findings / remember
- **Finding:** <durable fact worth keeping>

### Changes
- <what was built / changed this session — file or component level>

### Open threads
- <next action or unresolved question>
```

- **Evidence line is mandatory** — each entry `[[link]]`s the spec / ADR / PR (or branch) it
  corresponds to so the log reads as workflow evidence. If a link target doesn't exist yet, say so
  in Open threads (and see step 6's missing-artifact flag) — do not invent one.

## 3. Knowledge → `knowledge/notes/`
- Capture genuinely durable, reusable project knowledge as evergreen notes — one topic per
  file, in my voice, cross-linked with `[[wikilinks]]`. UPDATE an existing note rather than
  duplicating. Don't create notes for trivia; when it isn't durable, leave it in the log.

## 4. Lessons → `knowledge/lessons.md` (propose, then ask; newest at the bottom)
- Capture **anything genuinely learned the hard way this session** — not only "you did X, I told
  you Y instead" corrections, but also non-obvious gotchas, pitfalls, or findings that earned a
  durable rule. One line each, appended **at the bottom** (newest last, matching the file's
  convention): `[YYYY-MM-DD] | what happened / what was learned | rule or takeaway next time`.
- **Propose, then ask — never silently append.** Surface the candidate line(s) and ask me to
  confirm which to add (`AskUserQuestion`: per-candidate *Add* / *Skip*, or list them and let me
  choose). Append only the ones I approve.
- **Never manufacture entries** to look thorough. A candidate must be a real, reusable lesson — if
  nothing this session clears that bar, propose nothing and say so.

## 5. Living docs → `knowledge/docs/` — DRIFT-DETECT and FLAG ONLY (do NOT edit)
- These are pre-scaffold **design intent** now and become living docs once code exists — either
  way **I author them, never /wrap**. Diff each doc's claims against the current code/manifests and
  emit a checklist of exactly what drifted, pointing at the **doc + section**. Then **STOP** — I
  edit and stage the fixes.
- Cover: **PRD** (scope/goals) · **STACK** (deps/versions) · **ARCHITECTURE** (components/layers,
  the `IChatClient`/`IEmbeddingClient` split + their adapters, the in-memory cosine vector store,
  the `/backend` + `/frontend` layout) · **DATA-MODEL** (extracted fields:
  lessor/lessee/legal-description/royalty/effectiveDate; chunks; embeddings; citations) · **DATA-FLOW**
  (the ingest→extract→chunk→embed→retrieve→answer-with-citations pipeline) · **API** (endpoint
  contracts) · **RUNBOOK** (run/env) · **GLOSSARY** (terms) · **decisions/** (ADRs).
- Output format — a flag list, e.g.:
  `- DRIFT · knowledge/docs/STACK.md §Backend runtime — claims .NET 9; csproj now targets net10.0 → you update`
- **Do not** write to any `knowledge/docs/*` file, do not author ADR Decision/Consequences bodies, and
  do not regenerate Mermaid. Reason (state it inline in the report): these encode boundary
  decisions I must defend, and an auto-edit commits design judgment under my name.

## 6. Flag missing judgment artifacts (LIST — never author)
Surface, do not fix, any of these from this session:
- A **significant decision with no ADR** under `knowledge/docs/decisions/`.
- An **implementation with no preceding spec** in `knowledge/docs/specs/`, or **landed with no tests**.
- A **guardrail touched**: secrets handling, or `IChatClient`/`IEmbeddingClient` (interface or
  adapter wiring) changed **without a spec** in `knowledge/docs/specs/`.
- **Scope / PRD drift**: work that pushes past the slice into named out-of-scope territory
  (VNet/Private Link · Azure AI Document Intelligence OCR tuning · Azure AI Search · auth/RBAC ·
  observability stack), or that shifts the PRD's stated goals. **Report it; do not rewrite the goals.**
For each, name the artifact I should author and where it belongs. Authoring them is my call.

## 7. Confirm
- Report in 3–5 lines: what you logged, which notes/lessons changed, the doc-drift flags raised,
  the missing-artifact flags raised, and any **new files** (list their paths).

## 8. Commit ONLY what /wrap authored — confirm before commit/push
- Stage ONLY what this command wrote: `git add knowledge/logs knowledge/notes knowledge/lessons.md`. Do **not**
  `git add` any `knowledge/docs/` path at all — any `knowledge/docs/` file **I** already staged myself this
  session stays staged on its own and will be included in the commit without /wrap touching the
  index for it.
- **Accept gate (before committing or pushing):** show the exact staged set and the proposed commit
  message, and state whether it will push (i.e. the branch has an upstream). Then ask me —
  `AskUserQuestion`: *Commit* / *Revise message or scope* / *Don't commit*.
  - **Commit** → commit with a clean, descriptive message (**no** `Co-Authored-By` / "Generated
    with" trailer), then push **only** if the branch has an upstream.
  - **Revise** → adjust the message or what's staged per my feedback and ask again.
  - **Don't commit** → leave everything staged/uncommitted, and say so.
- If there are uncommitted **code** changes, list them so I can commit them deliberately — do NOT
  auto-bundle code into the doc/log commit. Mention env-var **names only**, never values. If
  there's nothing to commit, say so.
