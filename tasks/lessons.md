# Lessons — read at the start of every session; append what you learn the hard way.

Format (one line per lesson, newest at the bottom):
`[date] | what went wrong | rule next time`

[2026-06-06] | hardcoded "X installed" version facts drifted — ".NET 9" surfaced wrong twice (stated for ADR-0003; baked into the wiki-init template) when the box was on 10.0.108 | verify versions at runtime (`dotnet --version`); never assert an installed version in docs/templates
