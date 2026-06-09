# 0009 — RAG Answer-Quality Eval Harness

**Status:** Accepted

## What to build
An **on-demand evaluation harness** that measures the *quality of real answers to real questions* —
the thing the offline test suite deliberately can't. The existing `backend/tests/LandDoc.Tests`
suite proves the pipeline's *contract* with fakes (fake `IChatClient`, `LocalEmbeddingClient`,
in-memory store) and stays green on every commit. This spec adds a **separate harness that runs the
REAL pipeline end-to-end against the full production stack** — Azure OpenAI chat + embeddings + Azure
AI Search — over a curated subset of the `samples/` corpus, and **scores each answer** on three
metrics: **retrieval recall@k**, **citation/grounding faithfulness**, and **answer correctness**.

The dev-facing capability: a developer (locally) or a maintainer (via a manual CI job) runs the
harness; it ingests the curated corpus into an isolated Azure AI Search eval index, asks a fixed set
of questions through the real `/ask` path, grades the answers, and emits a **scored report** (console
+ HTML) plus a per-metric summary. Because real models are paid and non-deterministic, the harness is
**report-only by default** — it surfaces scores/trends rather than passing/failing — with an
**opt-in floor** that turns on hard per-metric threshold assertions when a quality gate is wanted.

Ground truth comes from `samples/` — `manifest.json` is the answer key (per-document ground-truth
fields), and the curated **cross-linked sets** (Henderson estate — Loving Co, TX; Whitaker tract —
Lea Co, NM; McKenzie Co, ND section) drive **multi-document** retrieval cases where one question's
answer spans several documents. Single-document field questions (royalty, bonus, lessor/grantor,
legal description, …) are graded directly against the manifest fields. This is a **read-and-measure**
harness: it never changes the application's behavior or its public ports, and it never touches the
live Azure AI Search index.

## Constraints
- **Backend / .NET:** new project **`backend/eval/LandDoc.Evals`** on .NET 10; C# conventions per
  `CLAUDE.md` (nullable, async end-to-end, file-scoped namespaces, `sealed`/`record` types). It holds a
  `ProjectReference` to `LandDoc.Api` and drives the app via `WebApplicationFactory<Program>` (modeled
  on `backend/tests/LandDoc.Tests/LandDocApiFactory.cs`) **without** the fakes — configured for the
  production providers instead.
- **Eval framework:** **`Microsoft.Extensions.AI.Evaluation`** + **`.Quality`** (LLM-judge evaluators)
  + **`.Reporting`** (response caching, result store, `aieval` HTML report). The `.Safety` package
  (Azure AI Foundry content-safety) is **out of scope**. This new dependency + the judge-model choice
  are recorded in an ADR (see Links) — the spec depends on that decision.
- **Metrics → evaluators:**
  - **recall@k** — a **custom deterministic `IEvaluator`** (`RecallAtKEvaluator`): for each case, did
    the expected source document(s) appear in the `/ask` response's `Citations` (matched by the
    citation's `Source` file name — `Citation.Source`, added by spec 0006)? No model call; fully
    reproducible. Unit-tested in the green suite.
  - **grounding/faithfulness** — `GroundednessEvaluator`, with `GroundingContext` = the concatenated
    `Citation.Text` of the answer's citations (i.e. only what the model was actually shown).
  - **correctness** — `EquivalenceEvaluator`, comparing the answer to the golden reference answer
    derived from `manifest.json` / the curated set.
- **Judge model (⚠ distinct port):** the evaluators need a **`Microsoft.Extensions.AI.IChatClient`**,
  which is **not** the project's custom `LandDoc.Api.Model.IChatClient`. The judge is a separate MEAI
  chat client pointed at **Claude Sonnet 4.6** (`claude-sonnet-4-6`) via the `Anthropic` SDK's MEAI
  adapter, wrapped in the library's `ChatConfiguration`. The **system under test** keeps using the
  project's own pipeline/ports unchanged; the two are independent.
- **Stack under test (full production):** config-selected via the existing `Program.cs` switches —
  `ModelClient:ChatProvider=azureopenai`, `ModelClient:EmbeddingProvider=azureopenai`,
  `VectorStore:Provider=azuresearch`. **No application code change** — the harness only supplies config
  + real keys.
- **Index isolation + teardown:** the harness ingests into a **dedicated eval index**
  (`landdoc-eval-{runId}`, via `Search:IndexName`) and **deletes it on completion** (Azure
  `SearchIndexClient.DeleteIndexAsync`, reusing `SearchOptions`), in the fixture's dispose/teardown so
  **local runs clean up too**. The live index is **never** read or written. *(assumption: cleanup is
  best-effort with a logged warning if the delete fails — a leaked `landdoc-eval-*` index is harmless
  and uniquely named.)*
- **Corpus:** a **focused curated subset (~15–25 docs)** of `samples/leases/*.pdf` — the three
  cross-linked sets (~9 docs: `22/27/28` Henderson, `03/24/35` Whitaker, `05/30/36` McKenzie) plus
  ~10 single-document field cases that double as retrieval distractors. The subset, the question set,
  and the golden answers live in the eval project's dataset (`Dataset/`), referencing the `samples/`
  PDFs by path and deriving golden values from `manifest.json`. *(assumption: ~15–20 questions total,
  mixing single-doc field lookups and multi-doc cross-set questions.)*
- **Invocation (both):**
  - **Local** — `dotnet test backend/eval/LandDoc.Evals` (or the project's run entry), with real
    secrets from `dotnet user-secrets` / environment variables.
  - **CI** — a new **manual** `.github/workflows/eval.yml` (`workflow_dispatch` only) that OIDC-logs
    into Azure (as `deploy.yml` does), reads the model/Search/Anthropic keys **from Key Vault** at run
    time, runs the harness against `landdoc-eval-${run_id}`, generates the `aieval` HTML report, and
    uploads it as an artifact.
- **Gating:** **report-only by default** (the run succeeds and emits scores + report); an **opt-in
  floor** (config/env flag, e.g. `Eval:Thresholds:Enabled=true` with per-metric minimums) turns on
  hard assertions that fail the run — locally and in CI — when a metric falls below its floor.
- **Green-suite isolation (critical):** `LandDoc.Evals` is **excluded from `LandDoc.slnx`** (and from
  `ci.yml`'s `dotnet test LandDoc.slnx`), so the PR/green gate **never needs real keys** and stays
  offline + deterministic. The only eval code in the green suite is the **deterministic unit tests**
  for `RecallAtKEvaluator` and the dataset loader.
- **Lock files:** `ci.yml` restores `--locked-mode`; the new eval packages require a regenerated
  `packages.lock.json` for the eval project (CI doesn't restore it, but local builds do).
- **Secrets:** never committed — local via `dotnet user-secrets` / env; CI via Key Vault (ADR-0016).
  The dataset commits **no** secrets and **no** PII (the `samples/` corpus is synthetic by design).
- **Out of scope:** changing any public port (`IChatClient` / `IEmbeddingClient` / `IVectorStore`) ·
  the `.Safety` content-safety evaluators · making the eval part of the PR green gate · auth/RBAC ·
  observability stack · Azure AI Search beyond the Free-tier vector store (semantic ranker / reranking
  stays out, ADR-0017) · regenerating or changing the `samples/` corpus itself.

## How to verify
- **Green suite still offline + green:** `cd backend && dotnet test LandDoc.slnx` passes, **including**
  the new deterministic `RecallAtKEvaluator` + dataset-loader unit tests, and needs **no** API keys.
- **Isolation check:** `LandDoc.Evals` does **not** appear in `LandDoc.slnx`'s test run (grep the
  solution / confirm `dotnet test LandDoc.slnx` doesn't execute eval scenarios).
- **Local eval run (real stack):** with real secrets set, running the eval project ingests the curated
  subset into a fresh `landdoc-eval-*` index, asks every question through `/ask`, and records all three
  metrics per question; on completion the **eval index is deleted** (verify it's gone from Azure AI
  Search).
- **Metric correctness (representative cases):**
  - *recall@k, multi-doc:* a Henderson-estate question (spanning `22/27/28`) yields citations whose
    `Source` file names include the expected source docs → recall@k scores as expected; a question whose
    answer is in one specific doc retrieves that doc.
  - *grounding:* `GroundednessEvaluator` scores a well-grounded answer high and a deliberately
    unsupported claim low, using only the cited passages as `GroundingContext`.
  - *correctness:* `EquivalenceEvaluator` scores an answer matching the manifest field (e.g. royalty
    `one-fourth (1/4)` for `01-ogl-midland-tx`) high.
- **Report:** the `aieval` HTML report renders per-question recall@k, groundedness, and equivalence
  scores plus an aggregate summary.
- **Gating modes:** with thresholds **off** (default) the run succeeds regardless of scores; with the
  **opt-in floor on**, a metric below its configured minimum makes the run fail (red), locally and in
  CI.
- **CI:** `eval.yml` triggers via `workflow_dispatch`, authenticates via OIDC + Key Vault, runs the
  harness, and uploads the HTML report artifact; the **live** Search index is untouched throughout.

## Links
- **Depends on (decision):** [[knowledge/docs/decisions/0020-llm-eval-harness-and-judge-model]] —
  records the choice of `Microsoft.Extensions.AI.Evaluation` as the eval framework and Claude Sonnet
  4.6 as the judge model. _To be recorded via `/adr` alongside this spec (spec + ADR first)._
- **Exercises (specs):** [[knowledge/docs/specs/0001-document-ingestion-write-path]] (the `/documents`
  write path the harness ingests through) · [[knowledge/docs/specs/0002-rag-qa-with-citations]] (the
  `/ask` read path + `AskResponse`/`Citation` shape the metrics read) ·
  [[knowledge/docs/specs/0004-extract-retrieval-service]] (the retrieval seam recall@k measures).
- **ADRs:** [[knowledge/docs/decisions/0017-azure-ai-search-free-tier-live-vector-store]] (the live
  store the eval index is isolated from) ·
  [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]] +
  [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]]
  (the production chat/embedding adapters under test) ·
  [[knowledge/docs/decisions/0014-surface-source-document-identity-in-ask-grounding-context]] +
  [[knowledge/docs/decisions/0009-corpus-wide-ask-retrieval-scope]] (document identity in citations,
  which recall@k keys on) · [[knowledge/docs/decisions/0016-single-container-azure-container-apps-keyvault-secrets]]
  (Key Vault as the CI secret source).
- **Corpus:** `samples/` (synthetic land/title docs) + `samples/manifest.json` (the answer key).
- **Docs to reconcile on merge:** `ARCHITECTURE.md` (note the eval harness as a non-prod, on-demand
  subsystem) · `RUNBOOK.md` (how to run the eval locally + required secrets) · `CICD.md` (the manual
  `eval.yml` workflow + Key Vault secret names).
- **Implementing PR:** _TBD — link once opened._
