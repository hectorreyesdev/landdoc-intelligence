# LandDoc.Evals — RAG answer-quality eval runner

> 📘 **Full operations guide** (setup, Azure/Foundry touchpoints, troubleshooting, from-scratch,
> baseline results): [`knowledge/docs/EVAL-HARNESS.md`](../../../knowledge/docs/EVAL-HARNESS.md).
> This README is the quickstart.

On-demand evaluation harness (spec 0012 / ADR-0021) that runs the **real** pipeline end-to-end against
the **full production stack** (Azure OpenAI chat + embeddings, Azure AI Search, Azure Blob) over a
curated subset of `samples/leases/*.pdf`, and scores each answer on three metrics:

| Metric | Evaluator | Model call? |
|---|---|---|
| **recall@k** | `RecallAtKEvaluator` (custom) — expected source file names vs the answer's `Citation.Source` | no (deterministic) |
| **groundedness** | `GroundednessEvaluator` (Microsoft.Extensions.AI.Evaluation.Quality) — context = the cited passages | yes (judge) |
| **correctness** | `EquivalenceEvaluator` — answer vs the golden `expectedAnswer` | yes (judge) |

The LLM **judge** is Claude **Sonnet 4.6** (`Eval:JudgeModel`), wired as a `Microsoft.Extensions.AI.IChatClient`
via the Anthropic SDK's `AsIChatClient(...)` — distinct from the project's own `IChatClient` port.

> ⚠️ **Not part of the green suite.** This project is deliberately **excluded from `LandDoc.slnx`** and
> from CI's `dotnet test LandDoc.slnx`. It needs real keys and calls paid, non-deterministic models, so
> it must never gate a PR. The deterministic bits it builds on (recall@k math, dataset loader) live in
> `LandDoc.Evals.Core` and *are* unit-tested in the green suite.

## Run it locally

Requires real secrets (set via `dotnet user-secrets` on `LandDoc.Api`, or environment variables):

| Secret | Env var | Purpose |
|---|---|---|
| `Anthropic:ApiKey` | `Anthropic__ApiKey` | the Sonnet judge |
| `AzureOpenAI:Endpoint` / `AzureOpenAI:ApiKey` | `AzureOpenAI__Endpoint` / `AzureOpenAI__ApiKey` | live chat + embeddings (SUT) |
| `Search:Endpoint` / `Search:ApiKey` | `Search__Endpoint` / `Search__ApiKey` | Azure AI Search (eval index + teardown) |
| `Blob:ServiceUri` **or** `Blob:ConnectionString` | `Blob__ServiceUri` / `Blob__ConnectionString` | Azure Blob document store |

Then, from the repo root:

```bash
# report-only (default): records scores, never fails on quality
dotnet test backend/eval/LandDoc.Evals

# opt-in quality gate: fail the run when a metric is below its floor (see appsettings.eval.json)
dotnet test backend/eval/LandDoc.Evals -e Eval__Thresholds__Enabled=true
```

Each run ingests the curated corpus into a fresh, isolated Search index `landdoc-eval-{runId}`, asks every
question through `/ask`, records the three metrics per case to a disk result store under the test output's
`eval-results/`, and on completion **tears everything down** — every ingested document is removed via
`DELETE /documents/{id}` (Blob + chunks) and the eval index is deleted. The live `landdoc-chunks` index is
never touched. (You can build the project without keys, but the scenarios can only **run** with them.)

### HTML report

The disk result store is compatible with the `aieval` report tool:

```bash
dotnet tool install --global Microsoft.Extensions.AI.Evaluation.Console
aieval report --path <test-output>/eval-results --output eval-report.html
```

## Dataset

`Dataset/questions.json` (~18 cases): single-document field lookups, the three cross-linked multi-document
sets (Henderson / Whitaker / McKenzie), and **absent-answer** cases that test the no-hallucination path
(`absent-*` ids; `expectedAnswer` is the abstain string). Golden values come from `samples/manifest.json`.
