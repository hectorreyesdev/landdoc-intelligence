# Workflow harness — commands + skills

The *what-we-know* view of how this repo's build harness works; the individual command/skill files
hold the operational detail.

## The four workflow commands share a spine
`/wiki-init`, `/adr`, `/spec`, `/reconcile` follow **clarify → draft-in-full → accept → act**:
- **Clarify** genuine ambiguity first — ask only if getting it wrong would change the output; don't over-ask.
- **Draft the judgment in full** — no `> [!note] AUTHOR:` deferral; flag minor open defaults inline as `*(assumption: …)*`.
- **Accept gate** (Accept / Revise / Keep) before the irreversible step.
- **Act:** `/wiki-init` writes the scaffolded doc tree (accept per group); `/adr` flips Status→Accepted + propagates links; `/spec` flips→Accepted + commits spec-first; `/reconcile` applies the ratified edit as a staged diff.

The AUTHOR-marker deferral pattern (seed a `> [!note] AUTHOR:` blank, human fills later) is **retired** —
every command drafts judgment in full and ratifies at the accept gate instead.

`/wrap` is the **bookkeeper**, not an author: it clarifies (bounded), writes only low-judgment
narrative (logs/notes/lessons), **flags** doc drift + missing artifacts for the trio to author, and
confirms before commit/push.

## The iron rule — author vs enforce
Public repo, committed under my name. `wiki/docs/*` and ADRs encode boundary decisions, so **only the
human authors/stages them**. `/wrap` and `/reconcile` may flag or regenerate-as-diff but never
silently commit design judgment. Accepted ADRs are **immutable** — a changed decision is a *new*
superseding ADR via `/adr`; the old one's Status becomes `Superseded by NNNN`, cross-linked both ways.

## ADR conventions
- Numbered `NNNN-slug` in `wiki/docs/decisions/`; Status `Proposed | Accepted | Superseded by NNNN`.
- Every ADR is linked from the `wiki/README.md` index (the canonical list) **and** ≥1 content doc.
- Doc-side link convention: `see [ADR-NNNN](decisions/NNNN-slug.md)`.

## TDD skill
`.claude/skills/tdd/` auto-engages on `/backend` or `/frontend` code work: new behavior ships with
tests, the suite stays green (`dotnet test` / `npm test`), the governing spec is offered (not
blocked), and lessons/ARCHITECTURE/ADRs are honored first. Backend full discipline; frontend pragmatic.

## GitHub access
Use the authenticated `gh` CLI for GitHub work — no github MCP plugin needed (it was redundant + failing, and removed).
