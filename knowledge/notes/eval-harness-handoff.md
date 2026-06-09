# Handoff — RAG Answer-Quality Eval Harness (spec 0009 / ADR-0020)

> Transient handoff note for continuing the eval-harness build in a fresh session. Safe to delete once
> the harness is complete. The authoritative design is **spec 0009** + **ADR-0020** (committed); this
> doc is the working state + gotchas. The original approved plan is at
> `/root/.claude/plans/what-s-going-on-snazzy-kernighan.md` (not in the repo — re-read this doc instead).

## Branch
All work is on **`claude/test-harnesses-explanation-u0rdts`**. Develop, commit, and push there only.
Do **not** open a PR unless the user asks.

## Goal (what we're building)
An **on-demand eval harness** that runs the REAL pipeline (full production stack: Azure OpenAI chat +
embeddings + Azure AI Search) over a curated subset of the `samples/` corpus and scores answers on
three metrics — **retrieval recall@k**, **citation/grounding faithfulness**, **answer correctness**.
It is **separate from the offline green suite** (which uses fakes and stays free/deterministic). This
is *not* unit testing — it measures real answer quality with real, paid, non-deterministic models.

## Locked decisions (see spec 0009 + ADR-0020)
- **Framework:** `Microsoft.Extensions.AI.Evaluation` + `.Quality` + `.Reporting` (NOT `.Safety`).
- **Metrics → evaluators:** recall@k = custom deterministic `IEvaluator`; grounding =
  `GroundednessEvaluator` (GroundingContext = concatenated citation texts); correctness =
  `EquivalenceEvaluator` vs golden answer.
- **Judge model:** Claude **Sonnet 4.6** (`claude-sonnet-4-6`) via a **`Microsoft.Extensions.AI.IChatClient`**
  — ⚠️ this is the MEAI `IChatClient`, NOT the project's custom `LandDoc.Api.Model.IChatClient`. The
  judge is wired independently; SUT ports are unchanged.
- **Stack under test:** full prod via config (`ModelClient:ChatProvider=azureopenai`,
  `ModelClient:EmbeddingProvider=azureopenai`, `VectorStore:Provider=azuresearch`). No app code change.
- **Index isolation:** ingest into a dedicated `landdoc-eval-{runId}` Azure AI Search index
  (`Search:IndexName`), and **delete it on teardown** (`SearchIndexClient.DeleteIndexAsync`). Never
  touch the live `landdoc-chunks` index.
- **Gating:** report-only by default; **opt-in floor** (config flag, e.g. `Eval:Thresholds:Enabled`)
  turns on per-metric threshold assertions.
- **Corpus:** focused ~15–25 doc subset (see Dataset below).
- **Invocation:** local (`dotnet test` on the runner project) + a **manual** `eval.yml` CI job
  (`workflow_dispatch`, OIDC + Key Vault). ⚠️ **CI workflow is ON HOLD** — user said do NOT apply the
  CI change yet. The `eval.yml` draft is in spec 0009 (and the plan file). Do not create
  `.github/workflows/eval.yml` until the user approves.

## Architecture: two projects (isolation is the point)
1. **`backend/eval/LandDoc.Evals.Core`** — pure, dependency-free classlib. Holds the framework-free
   bits so the GREEN SUITE can unit-test them with no eval-package/cloud dependency. **In `LandDoc.slnx`.**
   - `EvalCase.cs` — `record EvalCase(string Id, string Question, string ExpectedAnswer, IReadOnlyList<string> ExpectedSources)`.
   - `EvalDataset.cs` — `Parse(json)` / `LoadAsync(path)`; validates + throws early.
   - `RecallScoring.cs` — `RecallAtK<T>(expected, retrieved, comparer?)` → [0,1]; empty expected ⇒ 1.0.
2. **`backend/eval/LandDoc.Evals`** — the xUnit RUNNER. References `LandDoc.Evals.Core` + `LandDoc.Api`
   + the eval packages + `Anthropic`. **NOT in `LandDoc.slnx`** and **NOT in `ci.yml`'s `dotnet test`**
   — it needs real keys and must never run in the offline gate. ⬅️ **THIS IS THE REMAINING WORK.**

## ✅ DONE & COMMITTED (pushed)
1. `docs(spec+adr)`: spec 0009 + ADR-0020 + propagation (README decisions index, specs index, STACK row).
   Commit subject starts `docs(spec+adr): spec 0009 RAG answer-quality eval harness ...`.
2. `feat(eval)`: `LandDoc.Evals.Core` (3 source files) + added to `LandDoc.slnx` +
   `LandDoc.Tests` references it + `RecallScoringTests.cs` (7) + `EvalDatasetLoaderTests.cs` (7).
   **Green suite passes at 75 tests** (was 61), offline, and **`dotnet restore --locked-mode` passes**.

## 🔨 REMAINING WORK — the runner (`backend/eval/LandDoc.Evals`)
Files to create:
- ✅ **`LandDoc.Evals.csproj` — SCAFFOLDED** (this session). xUnit, net10.0, `<IsPackable>false</IsPackable>`.
  Pinned: eval packages **`10.6.0`** (latest STABLE/GA — confirmed on nuget.org; 9.5.0+ is GA, no preview),
  `Anthropic` 12.27.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.8, `Microsoft.NET.Test.Sdk` 17.14.1,
  `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4. ProjectRefs:
  `../LandDoc.Evals.Core` + `../../src/LandDoc.Api`. `dotnet restore` clean, `packages.lock.json` generated.
  Confirmed **NOT** in `LandDoc.slnx`. (`Directory.Build.props` forces the lock file on; CI doesn't restore it.)
- `RecallAtKEvaluator.cs` — implements `Microsoft.Extensions.AI.Evaluation.IEvaluator`; wraps
  `RecallScoring.RecallAtK` over the expected source **file names** vs the answer's `Citation.Source`
  values (case-insensitive). Returns a `NumericMetric`. **Confirmed shape** (10.6.0):
  `IReadOnlyCollection<string> EvaluationMetricNames { get; }` +
  `ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
  ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
  CancellationToken = default)`; `new NumericMetric(name, double? value, string? reason)`;
  `new EvaluationResult(params EvaluationMetric[])`.
- `SonnetJudgeChatClient` / `JudgeChatConfiguration.cs` — build a MEAI `IChatClient` for Sonnet 4.6.
  ✅ **RESOLVED — no hand-written adapter needed.** `Anthropic` 12.27.0 depends on
  `Microsoft.Extensions.AI.Abstractions` and ships the extension
  `Microsoft.Extensions.AI.AnthropicClientExtensions.AsIChatClient(this IAnthropicClient, string model, int? maxTokens = null)`.
  Use: `IChatClient judge = new AnthropicClient { ApiKey = key }.AsIChatClient("claude-sonnet-4-6");`
  then `new ChatConfiguration(judge)`. Model id from `Eval:JudgeModel` (default `claude-sonnet-4-6`),
  key from the existing `Anthropic:ApiKey` secret. (⚠️ this `IChatClient` is MEAI's, NOT the project's port.)
- `EvalPipelineFixture.cs` — `IAsyncLifetime` xUnit fixture. Boots `WebApplicationFactory<Program>`
  configured for the full prod stack + a unique `landdoc-eval-{Guid}` index (model on
  `tests/LandDoc.Tests/LandDocApiFactory.cs`, but WITHOUT the fakes — set config via env/inmemory
  overrides removed). Ingests the curated corpus once via `POST /documents`. Exposes an `HttpClient`.
  (No documentId↔fileName map needed — recall@k reads `Citation.Source` directly; see gotchas.) On
  dispose, **delete the eval index** (`Azure.Search.Documents.Indexes.SearchIndexClient.DeleteIndexAsync`),
  best-effort with a logged warning.
- `RagAnswerEvalScenarios.cs` — the eval cases. For each `EvalCase`: `POST /ask`, read
  `AskResponse(Answer, Citations[])`. Build a `ReportingConfiguration` with the 3 evaluators + a disk
  result store; run a `ScenarioRun` per case; record metrics. Match `ExpectedSources` (file names)
  against each `Citation.Source` for recall@k. Threshold assertions only when `Eval:Thresholds:Enabled`.
- `Dataset/questions.json` — the cases (see Dataset below). Add the curated `samples/leases/*.pdf` as
  `Content` `CopyToOutputDirectory` OR reference them by path from the repo `samples/` dir at runtime.
- `appsettings.eval.json` — provider selection + eval index name; **no secrets**.
- After creating: `dotnet restore backend/eval/LandDoc.Evals/LandDoc.Evals.csproj` (regenerates its
  own `packages.lock.json`; CI doesn't restore it). Then `dotnet build` to compile-check. ⚠️ You can
  **build** but **cannot run** the real eval here (no Azure/Anthropic keys) — verify compile only and
  document that the live run is gated on keys.

## ⚠️ Key code facts / gotchas (verified against the code)
- **`Citation` carries the source file name** — `(Guid ChunkId, Guid DocumentId, double Score, string Text, string Source)`
  (`backend/src/LandDoc.Api/Qa/Citation.cs`; `Source` is the file name — ADR-0014 follow-on / spec 0006).
  So recall@k matches the dataset's expected **file names** directly against each `Citation.Source`
  (case-insensitive) — **no `documentId ↔ fileName` map is needed**. (The earlier ingest-time
  map workaround — from when `Citation` had no `Source` — is obsolete; main has since added the field.)
- **Pipeline entry points:** ingest = `POST /documents` (multipart, field `file`); ask = `POST /ask`
  `{ "question": "..." }`. `AskResponse` = `{ answer, citations[] }`. See `Qa/AskEndpoints.cs`,
  `Ingestion/DocumentsEndpoints.cs`.
- **`Program` is `public partial class Program {}`** (end of `Program.cs`) so `WebApplicationFactory<Program>`
  works from the runner without InternalsVisibleTo.
- **Provider switches** (in `Program.cs`): `VectorStore:Provider` (azuresearch|inmemory),
  `ModelClient:EmbeddingProvider` (azureopenai|local), `ModelClient:ChatProvider` (azureopenai|anthropic).
  Env-var form uses `__` (e.g. `Search__IndexName`).
- **`manifest.json` shape:** `{ "documents": [ {entry}, ... ] }` (136 entries), each keyed by `id` with
  ground-truth fields (`lessor_or_grantor`, `lessee_or_grantee`, `royalty`, `bonus`, `effective_date`,
  `primary_term`, `acres`, `legal_description`, `doc_type`, `state`, `county`). This is the answer key.
- **Lock-file discipline:** `backend/Directory.Build.props` sets `RestorePackagesWithLockFile=true`, and
  `ci.yml` restores `--locked-mode`. After adding packages run
  `dotnet restore LandDoc.slnx --force-evaluate` then verify `--locked-mode` (lesson #28). The runner is
  not in the solution, so its lock file isn't gated by CI, but generate it anyway.
- **Green-suite isolation invariant:** `dotnet test LandDoc.slnx` must stay offline + green and need NO
  keys. The runner project must never be added to the solution.

## Environment setup (fresh container)
- **.NET SDK is NOT preinstalled.** Network is **allowlisted**: `nuget.org` ✅, `packages.microsoft.com`
  ✅, `archive.ubuntu.com` ✅, but `dot.net` / `builds.dotnet.microsoft.com` / `aka.ms` are **403**.
- Install the SDK via the Microsoft apt repo (Ubuntu 24.04 noble):
  ```bash
  curl -sSL -o /tmp/ms.deb https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
  dpkg -i /tmp/ms.deb
  # disable blocked PPAs first or apt-get update fails:
  mv /etc/apt/sources.list.d/*deadsnakes* /etc/apt/sources.list.d/*ondrej* /tmp/ 2>/dev/null
  apt-get update && apt-get install -y dotnet-sdk-10.0   # installs 10.0.108
  ```
- `Bash` tool note: `cd` **persists** across calls and a failed compound `cd` can leave you in the wrong
  dir — prefer absolute paths (repo root: `/home/user/landdoc-intelligence`).

## Dataset — curated subset + golden answers (from manifest.json)
Use these `samples/leases/*.pdf`. **Multi-doc cross-linked sets** (good for recall@k > 1 source):
- **Henderson estate, Loving Co TX** — shared legal description `Section 30, Block C-24, PSL Survey,
  Abstract No. 612`, 640 acres. Docs: `22-title-opinion-loving-tx`, `27-affidavit-heirship-loving-tx`,
  `28-probate-order-loving-tx`, `34-release-loving-tx`. (Probate: decedent **Andrew J. Henderson**,
  devisee **Carl A. Henderson**; title-opinion lessee **Delaware Basin Resources, LP**.)
- **Whitaker, Lea Co NM** — ⚠️ tract differs across docs! `03-ogl-lea-nm` & `35-ratification-lea-nm`
  share tract `Township 20 South, Range 37 East, N.M.P.M., Section 16: SE/4`, royalty **3/16**. The
  `24-amendment-lea-nm` is a **different** tract (Sec 33 W/2) and raised royalty to **1/4** — keep
  questions unambiguous (ask about the *original* lease ⇒ 3/16 ⇒ sources 03,35; or the *amended*
  royalty ⇒ 1/4 ⇒ source 24).
- **McKenzie Co ND, Section 22** — shared `Township 150 North, Range 98 West, 5th P.M., Section 22`,
  operator/lessee **Bakken Ridge Energy, Inc.** Docs: `05-ogl-mckenzie-nd`, `30-joa-mckenzie-nd`,
  `36-afe-mckenzie-nd`.

**Single-doc field cases** (graded straight from manifest; double as retrieval distractors):
- `01-ogl-midland-tx`: lessor `Margaret A. Caldwell, a single woman`; lessee `Llano Estacado Operating, LLC`;
  royalty `one-fourth (1/4)`; bonus `$1,500.00 per net mineral acre`; primary term `three (3) years`;
  legal `Section 14, Block 39, T-2-S, Texas & Pacific Ry. Co. Survey, Abstract No. 1187`.
- `02-ogl-reeves-tx`: lessor `The Holloway Family Trust dated June 3, 1998`; lessee `Delaware Basin Resources, LP`;
  royalty `22.5% (9/40)`; primary term `five (5) years`.
- `04-ogl-eddy-nm`: lessor `Pecos Valley Land Company, a New Mexico corporation`; lessee `Mesa Verde Resources, LP`;
  royalty `one-fifth (1/5)`; bonus `$1,750.00 per net mineral acre`.
- `03-ogl-lea-nm` (Whitaker original): royalty `three-sixteenths (3/16)`; lessee `Mesa Verde Resources, LP`;
  bonus `$1,000.00 per net mineral acre`; primary term `three (3) years`.
- `05-ogl-mckenzie-nd`: lessor `Arnold T. Bergstrom and Carol J. Bergstrom, as joint tenants`; royalty
  `18.75% (3/16)`; bonus `$900.00 per net mineral acre`; primary term `five (5) years`.

`questions.json` shape (matches `EvalCase`): array of
`{ "id", "question", "expectedAnswer", "expectedSources": ["<file>.pdf", ...] }`. Aim ~15–20 cases:
~5 single-doc field lookups + the 3 multi-doc cross-set questions + a few more single-doc distractors.
Re-pull any field with:
`python3 -c "import json;d={x['id']:x for x in json.load(open('samples/manifest.json'))['documents']};print(d['01-ogl-midland-tx'])"`

## Verification (end state)
1. `cd backend && dotnet test LandDoc.slnx` → green, offline, no keys (incl. the 14 eval-core tests).
2. `dotnet restore LandDoc.slnx --locked-mode` → passes (CI gate).
3. Runner compiles: `dotnet build backend/eval/LandDoc.Evals` (restores eval packages from nuget).
4. Confirm `LandDoc.Evals` is NOT in `LandDoc.slnx`.
5. (Cannot run live here — no keys.) Document the local run: set `Anthropic:ApiKey`, `AzureOpenAI:*`,
   `Search:*` via user-secrets/env, then `dotnet test backend/eval/LandDoc.Evals`; expect a
   `landdoc-eval-*` index created → questions answered → 3 metrics recorded → index deleted; `aieval`
   HTML report renders.
6. `eval.yml` CI job — **HELD** by user; do not create it yet.

## ✅ Eval-framework API — CONFIRMED against restored 10.6.0 (was the #1 open risk)
De-risked by scaffolding the runner csproj, restoring, and compiling a throwaway probe that referenced
every type (then deleted it). Verified from the package XML docs + a green `dotnet build`:

- **Custom evaluator:** `IEvaluator` (ns `Microsoft.Extensions.AI.Evaluation`) — see RecallAtKEvaluator
  bullet above for the exact `EvaluateAsync` signature + `NumericMetric` / `EvaluationResult` ctors.
- **Built-in evaluators (ns `…Evaluation.Quality`):** `new GroundednessEvaluator()` / `new EquivalenceEvaluator()`
  — **default (parameterless) ctor**, both implement `IEvaluator`.
- **EvaluationContext inputs (the per-metric extra data, passed as `additionalContext`):**
  - grounding → `new GroundednessEvaluatorContext(string groundingContext)` (prop `GroundingContext`) —
    feed it the concatenated `Citation.Text` of the answer's citations.
  - correctness → `new EquivalenceEvaluatorContext(string groundTruth)` (prop `GroundTruth`) — the golden answer.
- **Judge wiring:** `new ChatConfiguration(IChatClient)` (ns `…Evaluation`). IChatClient via Anthropic
  `AsIChatClient(...)` (see judge bullet).
- **Reporting entry point (ns `…Evaluation.Reporting` + `…Reporting.Storage`):**
  `ReportingConfiguration` built by **`DiskBasedReportingConfiguration.Create(string storageRootPath,
  IEnumerable<IEvaluator> evaluators, ChatConfiguration chatConfiguration, … all later params optional)`**
  (⚠️ `Create` lives in `…Reporting.Storage`; first arg is a **string** path, not `DirectoryInfo`).
  Then `await reportingConfiguration.CreateScenarioRunAsync(scenarioName, iterationName?, tags?, …)` →
  `ScenarioRun` (is `IAsyncDisposable`); run per case with the `ScenarioRunExtensions.EvaluateAsync(...)`
  string/`ChatResponse` overloads, passing the two `EvaluationContext`s. `ScenarioRunResult.EvaluationResult`
  holds the metrics; the `aieval` report tool renders the HTML.
- **Result store:** `DiskBasedResultStore` / `IEvaluationResultStore` (used internally by the disk-based config).

## Open question for the next session
- (none blocking) — pick the curated ~15–20 dataset cases and wire the fixture/scenarios per the
  confirmed API above. Live run still gated on Azure/Anthropic keys; `eval.yml` CI remains HELD.
