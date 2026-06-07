# 0006. React + TypeScript frontend (over Blazor)

- Status: Accepted
- Date: 2026-06-06

## Context
The slice needs a small SPA: upload control → extracted-fields view → question box →
answer-with-citations. CLAUDE.md already names **React + TypeScript** as the frontend, but the
repo never recorded *why not Blazor* — and because the backend is .NET 10
([[knowledge/docs/decisions/0003-dotnet-10-lts]]), Blazor (a C#-everywhere web UI) is a genuine
alternative, not a strawman. This ADR records that fork.

Forces at play:
- **The API boundary is already HTTP/JSON.** The SPA talks to the modular monolith
  ([[knowledge/docs/decisions/0004-modular-monolith-over-microservices]]) over HTTP+JSON through a typed
  client (ARCHITECTURE "Key boundaries"). That boundary is framework-agnostic — it neither needs nor
  benefits from a single-language stack.
- **Conventional, demonstrable architecture.** A JS/TS SPA + JSON API is the mainstream split; for a
  vertical slice *(assumption: this doubles as a portfolio/demo piece)* showing that conventional
  shape has signalling value.
- **Ecosystem and familiarity.** React + TypeScript has the larger component/tooling ecosystem and,
  *(assumption)* stronger team familiarity than Blazor (WASM or Server).
- **Already-fixed conventions.** CLAUDE.md pins the TypeScript style this implies: `strict: true`,
  function components + hooks, one typed `fetch` client, explicit return types, no `any`.

Builds on [[knowledge/docs/decisions/0001-record-architecture-decisions]].

## Decision
We will build the frontend as a **React + TypeScript single-page application**, **not Blazor**
(neither Blazor WebAssembly nor Blazor Server). The SPA uses function components + hooks with
TypeScript `strict` enabled, and all backend calls go through a single typed API client wrapping
`fetch` (no ad-hoc `fetch` in components), communicating with the ASP.NET Core API over HTTP/JSON.
Exact build/test tooling is **not pinned here** *(assumption: Vite + Vitest are the likely picks,
per STACK's TODO rows)* — that's settled when `/frontend` is scaffolded. This is binding on the
slice's frontend; revisiting it (e.g. moving to Blazor) is a future superseding ADR.

## Consequences
- **Mainstream ecosystem + end-to-end UI type safety.** Access to the React component/tooling
  ecosystem; `strict` TS plus the typed API client give type safety from the wire to the view.
- **Clean, framework-agnostic boundary.** Keeps the existing "SPA ↔ API: HTTP/JSON only" boundary
  intact and lets the frontend ship as static assets, decoupled from the backend's runtime.
- **Tradeoff — two languages and toolchains.** We accept a C# backend *and* a TS frontend (two
  dependency ecosystems, more context-switching) instead of Blazor's single-language C# stack.
- **Given up — shared types across the wire.** Blazor would let client and server share C# models;
  here, DTOs are expressed twice (C# `record`s ↔ TS types), so **DTO drift is a real risk**.
  Mitigation: the single typed API client is the one place to keep them aligned *(assumption: code-gen
  of TS types from the API contract is a possible later step, out of scope now)*.
- **Avoided Blazor-specific costs.** No Blazor WASM initial-download / runtime cold-start weight; no
  Blazor Server persistent SignalR connection and server affinity.
- **Follow-on:** pin React / TypeScript / build-tool versions when scaffolding `/frontend` (the
  `TODO` rows in STACK.md).
