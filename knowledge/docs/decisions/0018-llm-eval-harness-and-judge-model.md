# 0018. Microsoft.Extensions.AI.Evaluation as the eval framework, Claude Sonnet 4.6 as the LLM judge

- Status: Accepted
- Date: 2026-06-09

## Context
[[knowledge/docs/specs/0006-rag-answer-quality-eval-harness]] (Accepted) calls for an on-demand
harness that measures the *quality of real answers to real questions* — something the offline green
suite (`backend/tests/LandDoc.Tests`) deliberately can't do. That suite proves the pipeline's
**contract** with fakes (fake `IChatClient`, `LocalEmbeddingClient`, `InMemoryVectorStore`) and stays
green, offline, and deterministic on every commit. Answer *quality* needs the real production stack
(Azure OpenAI chat per [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]],
Azure OpenAI embeddings per [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]],
Azure AI Search per [[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]]) and a
way to score free-text answers, which can't be an `x == y` assertion.

Two choices need recording. **(A) The eval framework.** Spec 0006 scores three metrics — retrieval
recall@k, grounding/faithfulness, answer correctness. Recall@k is deterministic (does the expected
source `DocumentId` appear in the `/ask` response's citations) and is hand-rolled as a custom
evaluator regardless. Grounding and correctness need an LLM judge. Options: a .NET-native framework
(`Microsoft.Extensions.AI.Evaluation`), a language-agnostic Python tool (RAGAS / promptfoo /
DeepEval), or fully hand-rolled judge prompts. The repo is an all-`Microsoft.Extensions.*`,
single-`/backend` .NET 10 codebase; pulling in a Python toolchain for evals would add a second
language, runtime, and CI path for a non-production subsystem. `Microsoft.Extensions.AI.Evaluation`
(v10.x, .NET 10) ships built-in `Quality` evaluators (`GroundednessEvaluator`,
`EquivalenceEvaluator`), a `Reporting` package (response caching, result store, `aieval` HTML report),
a clean `IEvaluator` extension point for the custom recall@k evaluator, and runs via the test runner —
the same `dotnet test` tooling already in use.

**(B) The judge model.** The framework's evaluators consume a **`Microsoft.Extensions.AI.IChatClient`**
— which is **not** the project's custom `LandDoc.Api.Model.IChatClient` (the two are different types;
the judge is wired independently and does not touch the system under test). Options for the judge:
Claude **Sonnet 4.6** (`claude-sonnet-4-6`), Claude **Opus 4.8** (`claude-opus-4-8`), or reusing
whatever model `ModelClient:ChatProvider` points at. Reusing the SUT's model couples the grader to the
graded (a model judging its own family/output is a weaker, self-confirming signal). Opus is the
strongest judge but the priciest per run, and every question incurs two judge calls (grounding +
correctness). Sonnet 4.6 is a strong grader at a fraction of Opus's per-call cost — the right balance
for a harness meant to be re-run often. This is a **non-production, on-demand** subsystem, explicitly
excluded from the green suite, so its cost/non-determinism never gates a PR. *(assumption: the judge
authenticates with the existing `Anthropic:*` key/secret already provisioned for the fallback chat
adapter — no new secret.)*

## Decision
We will adopt **`Microsoft.Extensions.AI.Evaluation`** (the `Microsoft.Extensions.AI.Evaluation`,
`.Quality`, and `.Reporting` packages; **not** `.Safety`) as the evaluation framework for the RAG
answer-quality harness, and use **Claude Sonnet 4.6 (`claude-sonnet-4-6`)** as the **LLM judge** for
the `GroundednessEvaluator` and `EquivalenceEvaluator`. The judge is a dedicated
**`Microsoft.Extensions.AI.IChatClient`** built over the `Anthropic` SDK's MEAI adapter and wrapped in
the library's `ChatConfiguration` — distinct from, and with no effect on, the project's custom
`IChatClient` / `IEmbeddingClient` / `IVectorStore` ports, which are unchanged. Retrieval **recall@k**
is a custom deterministic `IEvaluator` (no model call). This binds the new
`backend/eval/LandDoc.Evals` project only; it is **excluded from `LandDoc.slnx` and the CI green
suite**, runs on the full production stack against an isolated `landdoc-eval-{runId}` Search index, and
is **report-only by default** with an opt-in threshold floor (per spec 0006).

## Consequences
- **One language, one toolchain.** Evals live in the same .NET 10 / `dotnet test` world as the rest of
  `/backend`; no Python runtime or second CI path. The `Reporting` package gives caching + an HTML
  report for free.
- **Built-in evaluators + a clean seam.** Grounding and correctness come from maintained Microsoft
  evaluators; recall@k slots in via `IEvaluator`. Less judge-prompt plumbing to own.
- **Two judge calls per question** (grounding + correctness) at **Sonnet 4.6** rates — cheaper than
  Opus, but evals still cost real money and are non-deterministic, so the harness stays **off the PR
  gate** and report-only by default. Scores are read as **trends**, not pass/fail, unless the opt-in
  floor is enabled.
- **Two distinct `IChatClient` types coexist** — MEAI's (judge) and the project's custom port (SUT).
  This is a deliberate, documented gotcha; conflating them would wire the judge into the pipeline.
- **Judge independence accepted as a known limit.** Sonnet 4.6 judging an Azure-OpenAI-generated
  answer decouples grader from graded, but LLM-as-judge is still imperfect (bias, variance);
  thresholds (when enabled) must leave headroom. *(A future ADR could pin a different/upgraded judge —
  that would supersede this one.)*
- **New dependencies + config.** NuGet: `Microsoft.Extensions.AI.Evaluation[.Quality/.Reporting]` and
  the `aieval` report tool; a regenerated `packages.lock.json` for the eval project. The judge reuses
  the existing `Anthropic:*` secret; no new secret is introduced.
- **Model swap is config, not code.** The judge model id lives in config (`Eval:JudgeModel`), so moving
  to Opus or a newer Sonnet later is a config change, not an adapter rewrite.

## Notes for implementation (non-binding)
- Packages: `Microsoft.Extensions.AI.Evaluation`, `Microsoft.Extensions.AI.Evaluation.Quality`,
  `Microsoft.Extensions.AI.Evaluation.Reporting`; report tool `Microsoft.Extensions.AI.Evaluation.Console`
  (`aieval`).
- Judge wiring: `Anthropic` SDK → `Microsoft.Extensions.AI.IChatClient` → `ChatConfiguration`; model
  `claude-sonnet-4-6` via `Eval:JudgeModel`; key from the existing `Anthropic:ApiKey` secret.
- See spec 0006 for the harness shape, corpus subset, gating, and the `eval.yml` CI job.
