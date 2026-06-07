# Lessons — read at the start of every session; append what you learn the hard way.

A lesson is anything learned the hard way that's worth a durable rule — a correction, a gotcha, or a
non-obvious finding (not only "you did X, do Y instead"). `/wrap` proposes candidates and asks
before adding any.

Format (one line per lesson, newest at the bottom):
`[date] | what happened / what was learned | rule or takeaway next time`

[2026-06-06] | hardcoded "X installed" version facts drifted — ".NET 9" surfaced wrong twice (stated for ADR-0003; baked into the wiki-init template) when the box was on 10.0.108 | verify versions at runtime (`dotnet --version`); never assert an installed version in docs/templates
[2026-06-06] | a repo reorg broke path references inside all 7 immutable ADRs | treat path/location refs in Accepted ADRs as mechanically migratable on a reorg — immutability protects the decision, not file paths
[2026-06-06] | `grep -v` on a token also hid a file whose NAME contained it (wiki-init.md was skipped when excluding "wiki-init") | when renaming, match the path column or re-grep per-file — don't filter the whole output line
[2026-06-06] | spec 0002 (read path) silently depended on 0001 storing each chunk's Text, but 0001's tests only asserted vector/dimension/determinism — a vector-only store would pass 0001 and break 0002's citations | at a cross-spec seam, pin the produced data shape as a contract on the PRODUCING side and assert it there; don't just describe the dependency in the consumer
[2026-06-06] | a behavior fixed in a spec's Constraints prose (populated-store "not found", citations still shown) had no matching check in How-to-verify, so it could ship untested | every behavior stated in Constraints needs a named check in How-to-verify — especially the demo's trust beats (e.g. anti-hallucination)
