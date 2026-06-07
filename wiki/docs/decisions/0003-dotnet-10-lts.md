# 0003. Target .NET 10 (LTS) for the backend

- Status: Accepted
- Date: 2026-06-06

## Context
The backend is an ASP.NET Core Web API structured as a modular monolith (one process; the
`Ingestion`, `Extraction`, `Retrieval`, and `Qa` modules split by folder/namespace). The
solution and projects are not scaffolded yet, so the target framework version is still an open
choice that every project file, CI step, and SDK pin will inherit — it is cheapest to fix now,
before `/backend` exists.

The forces in play:
- **Support window** — this runtime carries the slice and whatever grows out of it, so the
  servicing horizon matters. Per Microsoft's published lifecycle, **.NET 8 (LTS) and .NET 9 (STS)
  both reach end of support on 2026-11-10**, whereas **.NET 10 (LTS) is supported through
  2028-11-14**. Choosing 8 or 9 today means building on a runtime that EOLs within months.
- **Organizational standard** — Antero's own platform guidance names **".NET 8/10 LTS"** as the
  sanctioned set; .NET 10 is the current LTS within that.
- **Language/runtime features** — a newer LTS brings the latest C# language version and BCL
  surface, traded against the ecosystem/tooling maturity of a brand-new major.
- **Cloud target** — the production path leans on Azure (Foundry gateway, Azure OpenAI
  embeddings, Azure AI Search out of scope); the runtime must be a first-class, supported target
  on Azure App Service / container hosts.

Builds on [[wiki/docs/decisions/0001-record-architecture-decisions]]. Recorded in
[[wiki/docs/STACK.md]] (runtime/version) and [[wiki/docs/ARCHITECTURE.md]] (the modular-monolith
shape this runtime hosts).

## Decision
We will target **.NET 10 (LTS)** for the backend. Every project uses the `net10.0` target
framework moniker, and the SDK is pinned at the repo root via `global.json` (a 10.0.x SDK with
`rollForward: latestFeature`) so contributors and CI converge on the same toolchain. The LTS
choice is the data-driven one: among the supportable options it has the longest runway
(2028-11-14 vs. the 2026-11-10 EOL shared by .NET 8 and 9) and it matches Antero's stated
".NET 8/10 LTS" standard. This decision is binding on all projects created in this repo;
moving to a newer major later is a future ADR that supersedes this one.

## Consequences
- **Longest support runway of the realistic options.** The slice is born on a runtime supported
  to 2028-11-14 — roughly two years past the .NET 8/9 EOL — so we aren't shipping onto a
  framework that goes unsupported within months.
- **C# 14 across the codebase.** Targeting `net10.0` makes C# 14 the default language version,
  available to every module without per-project overrides.
- **The .NET 10 SDK (10.0.x) is required** on every dev machine and CI runner. `global.json`
  pins it and fails fast where it's missing, so the requirement is explicit rather than implicit.
  _(Already satisfied on the primary dev box — `dotnet --version` → `10.0.108`. Any machine still
  on the .NET 9 SDK must upgrade before it can build.)_
- **A clean "data-driven version choice" talking point.** The version was selected against
  published Microsoft support dates and Antero's ".NET 8/10 LTS" standard — not by defaulting to
  whatever SDK happened to be installed.
- **Tradeoff accepted:** a brand-new major can mean some libraries/analyzers/tooling lag the
  runtime. Risk is low here given .NET's compatibility track record and the deliberately small
  dependency surface of the slice; revisit if a required package has no `net10.0`-compatible
  build.
