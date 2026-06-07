# 0004. Modular monolith over microservices

- Status: Accepted
- Date: 2026-06-06

## Context
LandDoc Intelligence is a **vertical slice, not production** — the goal is to prove the
end-to-end RAG flow (ingest PDF → extract fields → chunk → embed → retrieve → answer with
citations) with the simplest thing that works. That goal sets the forces on how the backend is
decomposed:

- **Scale is "prove it," not "run it."** There is no production load, no independent-scaling
  requirement, and a small team *(assumption: effectively a one-developer effort for the slice)*.
  Microservices' operational tax — multiple deployables, network hops between modules, service
  discovery, inter-service auth, distributed tracing — would be pure cost with no payoff here.
- **The infra microservices imply is already out of scope.** The observability stack, VNet/Private
  Link, and similar production hardening are explicitly excluded (CLAUDE.md "Out of scope"), so a
  microservices topology would have no supporting platform to run well on.
- **We still want clean internal seams.** A single process must not become a big ball of mud — the
  work splits naturally into four concerns (`Ingestion`, `Extraction`, `Retrieval`, `Qa`), and
  model access is already isolated behind ports.

Builds on [[knowledge/docs/decisions/0001-record-architecture-decisions]]. Runs on the runtime chosen in
[[knowledge/docs/decisions/0003-dotnet-10-lts]]; the model-access ports that keep boundaries clean come
from [[knowledge/docs/decisions/0002-split-model-access-into-chat-and-embedding-clients]].

## Decision
We will build the backend as a **single ASP.NET Core process — a modular monolith — not a set of
independently deployed microservices.** Modules are separated by folder/namespace (`Ingestion`,
`Extraction`, `Retrieval`, `Qa`) and communicate through **in-process calls**; cross-cutting model
access goes through the `IChatClient` / `IEmbeddingClient` ports. Module boundaries are kept strict
(no cross-module reach-through into another module's internals) so that, if production scale ever
demanded it, a module could be extracted into its own service as a *refactor* rather than a rewrite.
This is binding for the slice; revisiting it (e.g. splitting out a module) is a future ADR that
supersedes this one.

## Consequences
- **One build/test/run/deploy unit.** `dotnet build` / `dotnet test` / `dotnet run` cover the whole
  backend; local dev and end-to-end debugging happen in a single process with no inter-service
  setup.
- **No distributed-systems overhead.** In-process calls mean no network latency, serialization, or
  partial-failure handling between modules; none of service discovery, inter-service auth, or
  distributed tracing is needed.
- **The option to split later is preserved.** Strict namespace boundaries plus the model-access
  ports mean a module can be lifted into its own service without rethinking the design — the seams
  are already where a service boundary would go.
- **Tradeoff accepted:** modules cannot be scaled or deployed independently, and the process is a
  single failure/deploy unit. Boundary enforcement relies on **convention and review**, not process
  isolation — discipline is required to keep the modular monolith from eroding into a ball of mud.
- **Follow-on / risk:** if this ever goes to production and one concern (e.g. ingestion/embedding)
  needs separate scaling, that's a superseding ADR; the port seams and module split are the hedge
  that keeps that move cheap.
