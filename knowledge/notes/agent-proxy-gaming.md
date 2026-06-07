# Agent proxy-gaming — why the spec is the acceptance target

A principle for how I delegate build work to coding agents in this repo.

## The problem
When an agent optimizes for "make it look done," the easy win is to satisfy the *proxy* I can see
(a screenshot, a passing smoke run, plausible-looking markup) rather than the *behavior* I actually
want. UI work is especially prone to this — a screen can render perfectly and still be wired to
nothing, hardcode its data, or skip every degradation path. "It looks right" is a weak gate.

## The move
**Pin a crisp, machine-checkable acceptance target before the agent builds, and make that — not the
demo — the definition of done.** A good target names the observable truths the agent can't fake by
faking the surface:
- the exact states and transitions,
- the contracts it consumes (request/response shapes),
- the failure/degradation behavior per status,
- structural rules that resist gaming (e.g. "the typed client is the only module that calls
  `fetch`" — checkable by grep + a test), and
- acceptance checks phrased as things that are observably true or not.

Spec [[knowledge/docs/specs/0003-frontend-vertical-slice]] is the worked example: it deliberately
does **not** decompose the React build (that's well-trodden) — its whole value is being the acceptance
target that stops the agent from gaming the UI.

## Why it works
The target turns "trust the output" into "verify against a fixed bar." It also survives drift: when
`/ask` went from `501` to live, the *acceptance checks* stayed valid (the 501 path just became
defensive) — only the framing changed. Relatedly, mocked-client component tests + a fetch-discipline
test assert the wiring an eyeball can't.

Sibling idea: the deterministic CI gate (specs land green or not at all) — the agent's PR is compiled
and tested before human eyes, so "looks done" never substitutes for "is done". See [[ci-pr-review]].
