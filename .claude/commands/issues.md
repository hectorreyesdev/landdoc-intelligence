---
description: Draft and — on approval — create the GitHub issues that implement an accepted spec. Analyzes a suggested list or drafts its own, right-sizes scope, removes redundancy, references the spec + docs, and proposes cross-issue and cross-spec dependencies with a wave/parallel execution order. Never mentions @claude unless explicitly asked.
argument-hint: "<spec NNNN-or-slug> [+ optional suggested issues]"
---

# issues — turn a spec into a dependency-ordered set of GitHub issues

Produce, get approval for, then create the GitHub issues that track implementation of a spec in
`knowledge/docs/specs/`. Accepts a list of issues the human suggests, **analyzes** it (right-sizes
scope, flags redundancy, fills gaps) and offers its own judgment; if **no list is given, drafts the
set itself and asks for approval**. Every issue references its spec and the relevant docs. The set
ships with **dependencies** — among the new issues and against existing open ones — so the execution
order and what can run in parallel are visible. **Issues never mention `@claude` unless the human
explicitly asks.**

> [!important] Approval-gated; plan before you create
> Creating issues is an outward action. **Draft → get the human's approval → then create.** Ground
> every issue's scope and acceptance in the spec and repo — don't invent work. Right-size: one issue
> = one coherent, independently-verifiable slice. When a suggested issue is too large, propose a
> split; when it duplicates another (new or existing), say so and propose a merge/drop. Surface your
> reasoning **at the gate**, not after the issues exist.

## 0. Orient
- Project root = the git repo root. Issues track a spec in `knowledge/docs/specs/`. Resolve the
  **target spec** from `$ARGUMENTS` (a `NNNN` number or slug) or, if unstated, from the conversation
  — if still unclear, **ask** which spec.
- Read that spec end to end: **What to build**, **Constraints** (especially the *Out of scope*
  boundary), **How to verify** (these acceptance checks become the issues' acceptance), and **Links**.
  Read the docs it points at (ARCHITECTURE / DATA-MODEL / DATA-FLOW / API) and the `CLAUDE.md`
  guardrails so issue scope honors module/port boundaries.
- Identify the GitHub repo (owner/name) the issues belong to.
- Separate any **suggested issue list** in `$ARGUMENTS` from the spec reference.

## 1. Survey existing issues (redundancy + cross-spec dependencies)
- List the repo's **open** issues (and scan recently-closed where relevant) with the GitHub tools.
  Read enough of each to judge overlap.
- Detect the two cross-spec relationships that matter:
  - **This work is blocked by** an existing open issue (often from a prior spec) — an unfinished
    prerequisite.
  - **This work blocks** an existing open issue — something already filed is waiting on it.
- Flag any existing issue that **duplicates** a slice you're about to propose — prefer
  reusing/closing over creating a near-twin.

## 2. Build the issue set
Two modes:
- **Human supplied a list** → treat it as a proposal, not gospel. For each item decide: **keep** /
  **split** (scope too large — covers more than one coherent, independently-verifiable slice) /
  **merge or drop** (redundant with another suggested or existing issue) / **reword**. Add any
  **missing** slices the spec implies. Explain every change.
- **No list** → **draft the set yourself**: decompose the spec into the smallest coherent,
  independently-shippable slices, each mapping to part of *How to verify*. Prefer **vertical cuts**
  over layer-cuts; honor the spec's *Out of scope*.

Each proposed issue carries: a clear **title**; a one-paragraph **scope** (one slice); **acceptance**
drawn from the spec's *How to verify*; **references** to the spec file and the specific docs/ADRs it
touches; and a **label** (default `spec-NNNN` — *(assumption: confirm at the gate)*). **No `@claude`
mention** unless the human asked.

## 3. Dependencies & execution order
- Propose **dependencies** among the new issues, and between new issues and the existing open ones
  from step 1 — **both directions** (*blocked by* / *blocks*).
- Derive an **execution order** from the graph: group issues into **waves** — wave 1 = no unmet
  blockers (start now, in **parallel**); each later wave is unblocked by the prior one. Make the
  parallelizable sets explicit so it's obvious what can run at once.
- **Tooling note:** the available GitHub tools **cannot** set GitHub's *native* "blocked by" links
  (UI/API-only). So dependencies are recorded **as text** in each issue body (`Blocked by: #N` /
  `Blocks: #N`) — the durable source of truth — and the report (step 6) tells the human which native
  links to add in the UI for the board "Blocked" icon. Where a relationship is genuinely parent/child,
  a **sub-issue** may be used instead.

## 4. Approve (the gate)
- Present the full proposal: the **issue table** (title · scope · acceptance · spec/doc refs · label),
  the **dependency graph + wave/parallel order**, and every analysis call you made (splits, merges,
  drops, added issues, proposed labels).
- Ask the human to **accept / revise / cancel** (`AskUserQuestion`). On *revise*, apply and
  re-present. Create nothing until accepted. Confirm here whether `@claude` should be mentioned in the
  bodies (default **no**).

## 5. Create (on approval)
- Create the issues with the GitHub tools, each body in this shape (no `@claude` unless asked):
  ```
  **Spec:** knowledge/docs/specs/NNNN-<slug>.md
  **Docs:** <relevant ARCHITECTURE / DATA-MODEL / DATA-FLOW / API / ADR links>

  ## Scope
  <one vertical slice>

  ## Acceptance
  <the spec's How-to-verify checks this issue satisfies>

  ## Dependencies
  Blocked by: #N — <title>   ·   Blocks: #M — <title>   ·   (or: none — ready to start)
  ```
- **Two passes** for cross-references: create all issues first to learn their numbers, then update
  each body to replace dependency placeholders with the real `#N`. Link existing-issue dependencies
  (and sub-issues, if used) the same way.
- Apply the agreed label to each issue.

## 6. Report
- List the created issues (`#N — title`) with their **wave/parallel execution order** and the
  dependency edges between them.
- Spell out the **native "blocked by" links to set in the UI** (the tools can't), plus any existing
  issue that should now link to or from the new ones.
- Call out any issue that is **already blocked** by unfinished prior-spec work — don't start it yet.

## How this fits the other commands
- **`/spec`** — writes/ratifies the spec these issues implement; on acceptance it **offers to run
  `/issues`**.
- **`/adr`** — if right-sizing surfaces a real design decision, record it there, not buried in an issue.
- **`/wrap`** — at session end, logs the work and flags drift; issues created here are part of that record.
