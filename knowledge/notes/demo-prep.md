# Demo & Interview Prep — LandDoc Intelligence

> Personal prep artifact for presenting this repo in an interview for a **Senior Full Stack
> Software Engineer, AI-Enabled** role (foundational hire: ship modern .NET *and* stand up the
> team's AI-assisted development practice). Not a project design doc — it lives in `notes/` for
> convenience but speaks to *you*, not to the codebase.

## How to use this doc

The bar to clear is the one the hiring team states in their own words:

> *"Can the person who prompted the work explain how it works in a meeting without looking at it?
> If they can't, that's a signal they over-vibed it."*

So the goal of this prep is **not** to memorize answers — it's to internalize the *why* behind every
decision until you can defend it cold. Read the architecture section until you can draw the diagram on
a whiteboard from memory. Read the Q&A section until the answers feel like yours. Everything here
traces to the repo's own ADRs, specs, code, and `knowledge/lessons.md` — if they open a file and ask
"show me," you can.

---

## 1. The 2-minute demo script

**One-sentence pitch:**
> "LandDoc Intelligence is a deployed vertical slice that does retrieval-augmented Q&A over land and
> title documents — you upload a lease, it extracts the key fields, then you can ask questions in
> plain English and get answers with citations pointing back to the exact source text. What I actually
> want to show you is *how* it was built: spec-first, ADR-driven, every line agent-assisted but
> human-reviewed."

**Live click-through (have a sample lease ready):**

| Step | What you click | What you say |
|------|----------------|--------------|
| 1 | Upload a lease PDF | "Ingest parses the PDF, pulls structured fields, chunks the text, embeds each chunk, and stores the vectors. One POST." |
| 2 | Extracted-fields view appears | "These fields — parties, dates, legal description, county — came from the chat model. Notice extraction is *best-effort*: if the model is down, the chunks still store and the doc is still queryable." |
| 3 | Type a question ("Who is the lessee?") | "Now the question gets embedded with the *same* model, we pull the top-k most similar chunks, and the model answers grounded only in those passages." |
| 4 | Answer renders with citations | "Every answer carries at least one citation that resolves to a real stored chunk. If it can't cite, it doesn't answer — it errors. That's the anti-hallucination guarantee." |
| 5 | (Optional) Open the repo `knowledge/` tree | "And here's the part that matters for this role — the decisions, specs, and lessons are committed alongside the code. The knowledge base *is* part of the deliverable." |

**Scope honesty line to keep handy:** "This is a vertical slice, deliberately. It's deployed and real,
but I drew a hard line around production hardening — auth, observability, VNet — and I can tell you
exactly why and what I'd add first."

---

## 2. Architecture, explained for presenting

### The shape of it

```
┌─────────────────────────────────────────────────────────────────────┐
│  React + TypeScript SPA   (upload · fields · ask · cited answer)      │
│  one typed API client (src/api/client.ts) — the ONLY thing that fetch │
└───────────────────────────────┬───────────────────────────────────────┘
                                 │  /documents · /ask   (relative paths, single origin)
┌───────────────────────────────▼───────────────────────────────────────┐
│  ASP.NET Core Web API  —  .NET 10 LTS  —  ONE process (modular monolith)│
│                                                                         │
│   Ingestion ──> Extraction ──> Retrieval ──> Qa     (namespace modules) │
│   parse/chunk    fields         embed+topK    answer+cite               │
│                                                                         │
│   ┌── Ports (interfaces every module depends on) ──────────────────┐    │
│   │  IChatClient        IEmbeddingClient        IVectorStore        │    │
│   └──────┬───────────────────┬─────────────────────┬───────────────┘    │
│          │                   │                     │                     │
│   live / fallback     live / offline        live / offline              │
│   AzureOpenAIChat     AzureOpenAIEmbed       AzureAiSearch               │
│   AnthropicChat       LocalEmbed (FNV-1a)    InMemory (cosine)           │
└──────────┬───────────────────┬─────────────────────┬───────────────────┘
           │                   │                     │
       Azure OpenAI        (same)            Azure AI Search (Free tier)
       Key Vault + managed identity · single container on Azure Container Apps
```

### The four ideas you must be able to defend

**1. Modular monolith, not microservices (ADR-0004).**
One process, four modules split by namespace — `Ingestion`, `Extraction`, `Retrieval`, `Qa` — calling
each other in-process. Microservices would buy independent scaling I don't need at slice scale and cost
me deployment, discovery, inter-service auth, and tracing overhead. The module *boundaries* are real, so
if production scale ever demands it, extracting a module to a service is a refactor, not a rewrite.

**2. Ports & adapters — provider choice is config, not code (ADR-0002).**
Three ports, each with a **live** adapter and an **offline/test** adapter:

| Port | Live adapter (default) | Offline / fallback adapter | Why split this way |
|------|------------------------|----------------------------|--------------------|
| `IChatClient` | `AzureOpenAIChatClient` → `gpt-5.4-mini` (ADR-0012) | `AnthropicChatClient` → `claude-opus-4-8` | Two providers, different failover semantics. Swap = config (`ModelClient:ChatProvider`). |
| `IEmbeddingClient` | `AzureOpenAIEmbeddingClient` → `text-embedding-3-small` @ 256-d (ADR-0013) | `LocalEmbeddingClient` (FNV-1a hashing, deterministic, free) | **No Anthropic embeddings adapter — Anthropic has no embeddings endpoint.** That asymmetry is *why* chat and embeddings are separate ports. |
| `IVectorStore` | `AzureAiSearchVectorStore` (Free tier, 256-d HNSW + cosine) (ADR-0017) | `InMemoryVectorStore` (linear cosine scan) | Live store persists across restarts; in-memory keeps tests offline and deterministic. |

The headline: **switching from Azure OpenAI to Anthropic, or from Azure AI Search to in-memory, is a
config-section change and a DI line — never a code change in any module.** That's the thesis of the
whole repo, and it's directly testable.

**3. The RAG pipeline, both directions.**

*Ingest (write path — spec 0001):*
`POST /documents` → parse (PdfPig for PDF; UTF-8 for `.txt`/`.md`) → extract fields via `IChatClient`
(*best-effort* — failure returns 201 with empty fields, chunks still stored) → chunk (≈800 chars,
150 overlap) → embed each chunk → store in `IVectorStore`. Returns `{ id, fileName, status, fields[],
chunkCount }`.

*Ask (read path — spec 0002):*
`POST /ask` → embed the question with the **same** embedding client → retrieve top-k (k=8) across the
**whole corpus** (ADR-0009 — every citation carries `documentId` so you can trace which doc a claim came
from) → map chunks to `QaPassage` DTOs (the chat port never sees the storage `Chunk` type — clean
hexagonal boundary) → answer via `IChatClient`, grounded in those passages and labeling each by source
document (ADR-0014) → return answer + citations. **Empty store → 409, never a hallucinated answer.**

The invariant to name out loud: **cite-or-error.** Every answer carries ≥1 citation that resolves to a
real stored chunk; if it can't, it doesn't answer.

**4. Deployment is real, secrets are clean (ADR-0016, 0017).**
One Docker image (multi-stage: build the SPA, publish the API, copy the SPA into `wwwroot`) → single
container on **Azure Container Apps**, single origin (SPA + API on one port, so no CORS — ADR-0011).
Secrets come from **Azure Key Vault via managed identity** (`DefaultAzureCredential`) — nothing in
source, nothing in the image. CI/CD is **GitHub Actions with OIDC federated credentials** — passwordless,
no stored secret; on merge to `main` it builds the image and rolls a new revision. The live vector store
is **Azure AI Search Free tier** ($0, persistent).

### What's deliberately out of scope — and why that's a senior signal

Named and *stubbed*, not forgotten: auth/RBAC, observability stack, VNet/Private Link, Azure AI Search
beyond Free tier (semantic ranker/reranking), OCR tuning, prompt caching, multi-turn, streaming, runtime
chat-provider auto-failover, an LLM eval harness. The point to make: **drawing the scope line and writing
down why is the senior move.** "I can build all of that — I chose not to, here, because the goal was to
prove the end-to-end flow and the swappable-provider thesis, and every deferral is documented with what
it'd take to add."

---

## 3. How this repo was built — the agentic workflow story

This is the half of the role the JD spends the most words on ("operationalize AI-assisted development,"
"establish team norms and guardrails," "review standards for AI-generated code"). This repo is a working
demonstration of exactly that — and it lines up almost one-to-one with the team's own enablement note.

| Their stated practice | What's in this repo |
|-----------------------|---------------------|
| CLAUDE.md as the agent "constitution" | `CLAUDE.md` defines architecture, conventions, guardrails ("what NOT to touch"), and points to the doc tree. Read first, every session. |
| Spec-first ("two paragraphs before you prompt") | `knowledge/docs/specs/NNNN-*.md` — every feature starts as a spec with a testable "How to verify" section. `/spec` scaffolds them; spec is committed *before* the implementation. |
| Decisions are durable | 17 ADRs (Nygard format) in `knowledge/docs/decisions/`. **Immutable once Accepted** — a changed decision is a *new* ADR that supersedes the old (e.g., 0012/0013 superseded the Foundry-primary 0007/0010; 0016 superseded 0011's prod realization). Decision *history* is never rewritten. |
| Writer/Reviewer, human-review rule | The TDD discipline (the `tdd` skill auto-engages on any code change), plus the lessons log capturing what review caught. The "explain-it-in-a-meeting" bar is the acceptance gate. |
| lessons.md as a living, personalized runbook | `knowledge/lessons.md` — entries in `[date] | what happened | rule` format, accrued every session via `/wrap`. ~40 hard-won gotchas (determinism bugs, .NET 10 `.slnx` surprises, multipart-form antiforgery, RTL selector collisions, squash-merge ancestry). |
| Keep docs current | `/wrap` flags doc↔code drift; `/reconcile` closes it (human picks the direction per item). Docs are design intent, kept honest against the code. |

**The line that sells it:** "I didn't just *use* an agent to write code faster. I built a repo where the
agent is governed — a constitution, specs before code, immutable decision records, a lessons log that
makes the agent smarter every session, and a hard human-review gate. That's the difference between
leverage and liability, and it's the practice I'd stand up for your team on day one."

---

## 4. JD → evidence map

Use this to steer the conversation toward what you can show.

| JD requirement | Evidence in this repo |
|----------------|-----------------------|
| Modern .NET (.NET 8/10/Core), ASP.NET Core | ASP.NET Core Web API on **.NET 10 LTS** (ADR-0003), minimal-API endpoints. |
| React / TypeScript front-end, modern state | React + TS SPA (ADR-0006), `strict: true`, function components + hooks, one typed API client. |
| Own architecture & design decisions | 17 ADRs documenting every cross-cutting call with context and consequences. |
| AI-assisted / agentic coding tools | Entire repo built spec-first + agent-assisted + human-reviewed; CLAUDE.md governance; lessons log. |
| Review standards for AI-generated code | The human-review gate + TDD discipline + "explain-it-in-a-meeting" bar; specs' "How to verify" sections. |
| RAG, vector DBs, embeddings | The whole pipeline: chunk→embed→store→top-k→cite; `IVectorStore` over Azure AI Search + in-memory; 256-d embeddings. |
| LLM cost / token optimization | Model-per-call-type (cheaper model for extraction), prompt-caching strategy noted, Free-tier economics. |
| Azure (App Service / PaaS), IaC, CI/CD | Azure Container Apps + Key Vault + AI Search + Azure OpenAI; GitHub Actions OIDC CI/CD; `az`-driven deploy. |
| TDD / automated testing | xUnit + `WebApplicationFactory` (backend), Vitest + RTL (frontend); both pinned offline; tests assert spec invariants. |
| SDLC maturity in a less-structured env | The spec→ADR→code→test→wrap loop *is* a lightweight SDLC; branching, PR review, paths-filtered CI. |
| Ingest "vibe-coded" apps, manage consultancies | Talk track below — this repo's own guardrails are the template you'd impose on inherited/partner code. |
| Azure Foundry experience (nice-to-have) | ADR-0007 captured a Foundry-gateway design; superseded by direct Azure OpenAI (0012) for PAYG reasons — you can speak to *both* and *why you switched*. |

---

## 5. Anticipated questions & how to answer

> Format: **Q** → the answer to give → *honest edge* (what to concede; never bluff).

### Architecture & RAG

**Q: Why ports and adapters? Isn't that over-engineering for a slice?**
The opposite — it's what *lets* it stay a slice. The provider landscape is moving weekly; I wanted to
prove I could swap Azure OpenAI for Anthropic, or a real vector index for an in-memory one, without
touching a single module. Each port has a live adapter and an offline one, selected by config. The
payoff is concrete: my tests run fully offline against `LocalEmbeddingClient` + `InMemoryVectorStore`,
deterministically, no cloud creds — and production flips to Azure with a config section. *Edge:* for a
truly throwaway prototype I'd skip it; here the swappability *was* the thesis I wanted to demonstrate.

**Q: Why a monolith and not microservices?**
At one process and slice scale, microservices are pure tax — deployment, discovery, inter-service auth,
distributed tracing — for scaling I don't need. I kept strict module boundaries (`Ingestion`,
`Extraction`, `Retrieval`, `Qa`) so the option to extract a service later is a refactor, not a rewrite.
*Edge:* if `Extraction` became a heavy GPU workload with a different scaling profile, that's the first
candidate to pull out.

**Q: How do you stop the model from hallucinating answers?**
Two mechanisms. First, the model only ever sees the retrieved passages — it's grounded, not free-running.
Second, **cite-or-error**: every answer must carry at least one citation that resolves to a real stored
chunk, and an empty store returns 409 instead of an answer. The citation is the receipt. *Edge:* grounding
reduces but doesn't eliminate hallucination — a model can still misread a passage. The next step I'd add
is an LLM-as-judge eval that checks the answer is actually supported by the cited text.

**Q: Walk me through your chunking. Why 800 characters, 150 overlap?**
Fixed-size character windows with overlap so a fact spanning a boundary isn't lost. 800/150 is a starting
point tuned for short factual lookups on dense legal text, not a researched optimum. *Edge:* it's
naive — I'm not chunking on semantic or structural boundaries (clauses, sections). For production I'd
test structure-aware chunking and measure retrieval quality, not guess.

**Q: Why 256-dimension embeddings when the model emits 1536?**
`text-embedding-3-small` natively emits 1536-d but supports reducing via the `dimensions` parameter. 256
is plenty for this corpus and cuts memory and similarity-compute cost. The dimension is a single config
lever, and it's the same on both the live and offline adapters so cosine stays meaningful across paths.
*Edge:* changing it requires a full re-ingest — the index dimension is fixed at create.

**Q: In-memory vector store vs. Azure AI Search — when does in-memory break?**
In-memory is a linear scan — fine for a demo corpus, O(n) per query and lost on restart. Azure AI Search
(Free tier, HNSW index) gives me sub-linear search and persistence across container restarts and
redeploys, at $0. Same `IVectorStore` seam, config-selected. *Edge:* Free tier caps at 50 MB / 3 indexes
and I deliberately left semantic ranker/reranking out of scope — that's the first paid upgrade if recall
needed it.

**Q: How would you scale this to millions of documents?**
Move the vector store to a paid Azure AI Search tier (or a dedicated vector DB), add reranking, and
consider per-document or metadata-filtered retrieval instead of global top-k. The module boundaries mean
ingest could become its own scaled-out worker. The ports don't change — that's the point.

### Cost, model selection, token optimization

**Q: How do you control LLM cost?**
Three levers. Model-per-call-type — extraction can run a cheaper/faster model than the final answer.
Prompt caching for the repeated document context (designed for, deferred as an optimization). And the
embedding/vector path is cheap by design: local hashing for tests, small-dimension embeddings, Free-tier
search. All model IDs live in config, never hardcoded, so cost tuning is a config change. *Edge:* I
haven't wired token-level cost telemetry yet — that's exactly the "LLM observability" the JD lists as
future-state, and it's where I'd start.

**Q: You used Azure OpenAI GPT as the live model but kept Anthropic as fallback — why?**
Originally I designed around a Foundry gateway (ADR-0007). I switched the live default to Azure OpenAI
`gpt-5.4-mini` (ADR-0012) for a pragmatic reason: it's PAYG-eligible on the provisioned Azure stack,
whereas serving Claude through Foundry needed an Enterprise agreement I didn't have. Anthropic-direct
stayed as the config-swap fallback — which conveniently *proves* the provider abstraction works. *Edge:*
that's a real-world procurement constraint driving an architecture decision, and I documented it rather
than hiding it.

### Agentic development & team enablement

**Q: We want to stand up AI-assisted development for 2–3 devs. Where do you start?**
Exactly how the team's own note frames it, and how I built this repo: (1) shared CLAUDE.md per repo as the
agent's constitution; (2) the spec-first habit — a short spec before any prompting, it's worth more than
any tool; (3) Writer/Reviewer — code written in one context, reviewed in a fresh one; (4) the
non-negotiable human-review rule and the "can you explain it in a meeting" gate; (5) wire automated checks
(tests, lint) into CI; (6) only *then* layer on parallel agents and specialized subagents. Start with
discipline, add leverage second.

**Q: How do you keep AI-generated code from becoming unmaintainable slop?**
Governance, not vibes. Specs define intent before code; ADRs make decisions durable and reviewable; the
TDD gate means behavior ships with tests; the lessons log turns every mistake into a permanent rule the
agent reads next session; and a human reviews every line against the "explain-it" bar. The repo in front
of you is the proof — open any ADR or the lessons file.

**Q: How do you review code you didn't write line-by-line?**
The same way I'd review a strong contractor's PR: I read it against the spec's acceptance criteria and the
architectural guardrails, I run the tests, and I apply the gut check — if I can't explain how it works
without looking, it doesn't ship. Fresh-context review (a second agent or a second pass) catches what the
author's context bias misses.

### SDLC, standards & process maturity

**Q: How would you bring SDLC structure to an environment where it's immature?**
Lightweight and visible, not heavy. This repo *is* a minimal SDLC: spec → ADR → test-first code → CI →
session-wrap that logs lessons and flags doc drift. Source control with a real branching + PR-review flow,
paths-filtered CI so backend and frontend test independently, and IaC/CLI-driven, repeatable deploys.
Standards land as living docs the team can adopt, not a binder nobody reads.

**Q: How do ADRs and specs actually help a team, vs. slow it down?**
They're the cheapest way to stop relitigating settled decisions. An ADR is one page, immutable once
accepted; when a decision changes you write a *new* one that supersedes it and cross-link — so anyone can
trace the current call and *why* without archaeology. Specs front-load the "what and how-to-verify" so the
build (human or agent) has a target. The cost is minutes; the savings is every "wait, why did we do it
this way" conversation that never happens.

### Consultancy & vibe-code ingestion

**Q: We inherit code from consultancies and citizen developers. How do you bring it under control?**
Treat the inherited app like an untrusted PR against a standard it hasn't met yet. Concretely: get it into
source control, write a CLAUDE.md and a short spec capturing what it *actually* does, characterize it with
tests before changing anything (so I can refactor safely), run it through the same review/quality gates as
new code, and record an ADR for any decision I'm now inheriting or overturning. This repo's guardrails are
the template — I'd impose the same constitution + spec + lessons discipline on inherited code, hardening it
incrementally rather than rewriting blind. *Edge:* the honest first step is always "make it observable and
tested," because you can't safely change what you can't characterize.

### Leadership & behavioral

**Q: How do you evaluate a new AI tool — there's a new one every week.**
Data, not hype. Pick a real task, run the tool against it, measure outcome (quality, speed, cost/tokens),
and compare to the incumbent. Document the finding. The JD calls it "data-driven recommendations grounded
in hands-on evaluation" — that's exactly the habit. The ADR superseding pattern in this repo is the same
muscle: try, measure, decide, record, revisit when the facts change.

**Q: What's the failure mode of AI-assisted development you worry about most?**
Over-vibing — shipping code the author can't explain. It feels productive and quietly accrues liability.
The defense is the human-review gate and the "explain-it-in-a-meeting" test. I'd rather a dev ship less and
understand all of it than ship a feature they'd have to reverse-engineer in an incident.

### Curveballs / weaknesses

**Q: There's no auth. Isn't that a problem?**
Yes, for production — and it's a *deliberate, documented* omission, not an oversight. The slice's job was
the RAG flow and the swappable-provider thesis; auth/RBAC is explicitly out of scope in CLAUDE.md. First
thing I'd add for a real deployment, alongside observability. I can speak to how I'd do it (managed
identity is already wired; I'd add Entra ID auth at the ingress).

**Q: How do you know the answers are actually any good — where's the eval?**
I don't have an automated eval harness yet, and I'd flag that as the most important next investment. Today
the guarantee is structural (cite-or-error, grounded generation) and the tests assert the *contract*, not
answer *quality*. The JD lists LLM evaluation/observability as future-state — building an eval set with
LLM-as-judge scoring is exactly where I'd grow this.

**Q: What would you do differently if you started over?**
Structure-aware chunking from the start, a token-cost telemetry hook, and an eval harness scaffolded
early rather than deferred. The ports/adapters and the spec/ADR discipline I'd keep without question —
they paid for themselves.

---

## 6. Sharp one-liners

- "Swapping the model provider is a **config change, not a code change** — that's the whole thesis."
- "**Every answer carries a citation, or it errors.** No silent hallucination."
- "The **knowledge base is part of the deliverable** — specs, decisions, and lessons ship with the code."
- "Anthropic has no embeddings endpoint — that asymmetry is *why* chat and embeddings are separate ports."
- "ADRs are **immutable**; a changed decision is a new ADR that supersedes the old. Decision history is never rewritten."
- "Scoping out production hardening — and **writing down why** — is the senior move, not a gap."
- "The agent is **governed**: a constitution, specs before code, a human-review gate. Leverage, not liability."

---

## 7. Gotcha cheat-sheet (don't fumble these live)

| Fact | Value |
|------|-------|
| Runtime / framework | ASP.NET Core on **.NET 10 LTS** (C# 14) |
| Modules | Ingestion · Extraction · Retrieval · Qa (namespaces, one process) |
| Live chat model | Azure OpenAI **`gpt-5.4-mini`** (`ModelClient:ChatProvider=azureopenai`) |
| Fallback chat model | Anthropic **`claude-opus-4-8`** (config swap) |
| Live embeddings | Azure OpenAI **`text-embedding-3-small`**, **256-d** |
| Offline embeddings | `LocalEmbeddingClient`, FNV-1a hashing (deterministic) |
| Live vector store | **Azure AI Search Free tier**, 256-d HNSW + cosine (`VectorStore:Provider=azuresearch`) |
| Offline vector store | `InMemoryVectorStore`, linear cosine scan |
| Chunking | ≈**800 chars**, **150** overlap |
| Retrieval | top-**k = 8**, **corpus-wide** (no per-doc filter), each citation carries `documentId` |
| Endpoints | `POST /documents` (ingest), `POST /ask` (Q&A); empty store → **409** |
| Deployment | single container on **Azure Container Apps**, single origin (no CORS) |
| Secrets | **Key Vault via managed identity** (`DefaultAzureCredential`); none in source/image |
| CI/CD | **GitHub Actions + OIDC** (passwordless); merge to `main` → build image → roll revision |
| Key ADRs | 0002 split ports · 0004 monolith · 0009 corpus retrieval · 0012 Azure GPT chat · 0013 Azure embeddings · 0014 source-labeled grounding · 0016 ACA+Key Vault · 0017 AI Search live |
| Superseded ADRs | 0007/0010 (Foundry/Anthropic-primary) → 0012 · 0008 (hashing embeddings) → 0013 · 0011 (prod topology) → 0016 |
| Specs | 0001 ingest · 0002 RAG Q&A+citations · 0003 frontend slice · 0004 retrieval service · 0005 markdown/text ingest |
| Tests | xUnit + `WebApplicationFactory` (backend), Vitest + RTL (frontend); both pinned offline |
| Out of scope (named/stubbed) | auth/RBAC · observability · VNet/Private Link · reranking · OCR tuning · prompt caching · multi-turn · streaming · runtime provider failover · eval harness |
