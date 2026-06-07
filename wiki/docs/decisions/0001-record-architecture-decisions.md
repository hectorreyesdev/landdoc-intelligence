# 0001. Record architecture decisions

- Status: Accepted
- Date: 2026-06-06

## Context
We want a durable, reviewable record of the architecturally significant decisions on this project,
so future contributors understand *why* the system is shaped the way it is — not just *what* it is.

## Decision
We will record architecture decisions as ADRs in `wiki/docs/decisions/`, using the Nygard format
([0000-template.md](0000-template.md)). Each significant decision gets the next sequential number and
is captured as part of the work (and reconciled during `/wrap`).

## Consequences
- Decisions are traceable and reviewable alongside the code.
- New contributors read the decision history instead of reverse-engineering intent.
- Minor or easily-reversible choices don't need an ADR — keep the log signal-rich.
