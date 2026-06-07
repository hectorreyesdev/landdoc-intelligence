---
name: tdd
description: Test-driven development discipline for this repo's code. Use whenever writing, modifying, refactoring, or extending application code under /backend (.NET) or /frontend (React/TS) — implementing a feature, fixing a bug, or changing behavior. Ensures the existing test suite stays green (dotnet test / npm test), new behavior ships with tests, the governing spec is known (offers /spec if none — never blocks), and relevant lessons/ARCHITECTURE/ADRs are consulted and honored before code changes. Not for docs/wiki/spec/config-only edits.
---

# TDD workflow (this repo)

The discipline that governs **code** work here. It is **order-flexible** — you don't have to write
the failing test first, but the hard rules are non-negotiable: **every behavior change ships with a
test, and the full suite is green before the change is done.** It is **spec-aware** (offer, never
block) and **doc-aware** (honor lessons / ARCHITECTURE / ADRs). It guides; it does not hard-block —
when a gate isn't met, surface it and let me decide.

## When this applies
- Any time you write / modify / refactor / extend application code under `/backend` (.NET) or
  `/frontend` (React/TS) — a feature, a bugfix, a behavior change.
- **Not** for docs, `wiki/`, ADRs, specs, or config/comment-only edits — *but those must still not
  break the suite.*

## Before changing code — get oriented (lightweight)
1. **Know the governing spec.** Identify which `specs/NNNN-*.md` covers this work.
   - If you're unsure which spec applies, **ask me.**
   - If **no spec exists**, surface it and **offer to run `/spec`** to write one first — then proceed
     if I decline. Never block on this. (Trivial fixes rarely need a spec; features usually do.)
2. **Read the relevant context** so you don't repeat known mistakes or violate design intent:
   - `tasks/lessons.md` — past corrections + the rule each one set. Re-check anything touching this area.
   - The affected `wiki/docs/` — **ARCHITECTURE** (the module/seam you're touching), **DATA-MODEL** /
     **DATA-FLOW**, **API**.
   - Relevant **ADRs** in `wiki/docs/decisions/` — the decisions that constrain this code (the
     `IChatClient`/`IEmbeddingClient` ports, the in-memory cosine store, .NET 10, modular monolith,
     Foundry+Anthropic fallback). **Honor them.** If your change would contradict an **Accepted** ADR,
     STOP and flag it — that needs a superseding ADR via `/adr`; do not silently diverge.
   - The spec's **How to verify** section — those acceptance checks are your test targets.

## The cycle — order-flexible, always green
- **New behavior ships with tests.** Write tests and implementation together; what matters is that
  every behavior change is covered and the suite passes before you call it done. (Red-green-refactor
  is welcome but not required.)
- **Existing tests must pass.** Before any change is "done," run the suite and confirm green:
  - Backend: `dotnet test`
  - Frontend: `npm test`
  Run the affected project's suite after each meaningful change; run **both** if the change spans them.
- **Never weaken a test to get green.** If a change legitimately redefines expected behavior, update
  the test to the *new intended* behavior and make sure the spec/docs agree. Don't delete or skip a
  test to pass — if a test is genuinely wrong, say so and fix it deliberately.
- **Refactors** (no behavior change) need no new tests, but the existing suite must stay green throughout.

## Scope — backend vs frontend
- **Backend (.NET) — full discipline.** Unit-test module logic (`Ingestion` / `Extraction` /
  `Retrieval` / `Qa`), the model-access seams (test against `IChatClient` / `IEmbeddingClient`, with
  fakes — never a live provider in unit tests), the in-memory vector store, and the citation
  invariant (every answer carries ≥1 citation resolving to a stored chunk). xUnit, per STACK.
- **Frontend (React/TS) — pragmatic.** Test behavior and logic that matters — the typed API client,
  hooks, state, meaningful rendering — not snapshot-everything or trivial markup. Vitest, per STACK.
- **No test harness yet?** The repo is pre-scaffold. On the first feature that needs tests, set up the
  test project consistent with STACK (xUnit / Vitest) as part of that work, and say you did.

## Definition of done for a code change
- The governing spec is known — or its absence was surfaced and `/spec` offered.
- New/changed behavior is covered by tests, and the full suite (`dotnet test` and/or `npm test`) is **green**.
- The change honors the relevant ADRs / lessons / docs; any contradiction was **flagged, not silently made**.
- Guardrails respected: **no secrets in code**; a change to `IChatClient`/`IEmbeddingClient`
  (interface or adapter wiring) needs a spec in `specs/` — surface if missing.
- If the change makes a doc claim stale (new endpoint, entity, version…), note it — that's `/wrap`'s
  drift-flag + my edit, not a silent doc write.

## How this fits the other commands
- **`/spec`** — writes the spec this skill asks for (offered, not forced).
- **`/adr`** — record (or supersede) a decision your change would otherwise contradict.
- **`verify`** (skill) — drive the running app to confirm behavior end-to-end; complements unit tests.
- **`/wrap`** — at session end, logs the work and flags any missing tests/specs or doc drift.
