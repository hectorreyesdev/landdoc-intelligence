---
description: One-time idempotent scaffold of the landdoc-intelligence doc tree (root docs + wiki/) — clarifies the judgment with the human, drafts every doc in full from facts + that input, asks to accept, never clobbers.
argument-hint: "(no arg) — scaffold the doc tree once; re-run anytime to fill only what's missing"
---

# wiki-init — scaffold the landdoc-intelligence docs

Scaffold the documentation tree for THIS repo: root-level project files plus a `wiki/`
(living docs + evergreen notes + a COMMITTED session log). **Idempotent** — never clobber an
existing file; only create what's missing. Markdown + Mermaid (renders in Obsidian/GitHub).

This is a guided **design session**, not a silent generator. For each doc it follows the same spine
as the other workflow commands: **clarify → draft-in-full → accept.** It seeds derivable facts,
**clarifies the judgment with the human**, drafts each doc completely from facts + that input, and
asks the human to accept before the tree is final.

> [!important] Reach common understanding first; the human owns the judgment
> This is a public repo committed under the developer's name — he must be able to explain every line
> in an interview ("explain it in a meeting" gate). So **judgment is never invented**: product goals,
> scope, architecture rationale, ADR bodies come from **asking the human (step 2)** and are **ratified
> at the accept gate (step 4)**. You draft the wording in full from the human's answers + repo facts —
> you do not silently decide it, and you do not leave it blank. Reserve inline `*(assumption: …)*`
> only for low-stakes defaults the human can catch at the gate. Auto-*invented* design docs are the
> over-vibing trap this repo exists to refute; drafting-from-the-human's-answers is not that.

> [!important] Pre-scaffold reality — these docs are DESIGN INTENT right now
> There is **no app code yet** (the `/backend` + `/frontend` scaffold comes AFTER these docs). So
> ARCHITECTURE / DATA-MODEL / DATA-FLOW / API are written NOW as the **design the scaffold will
> implement** — not as descriptions of existing code. **Verify the installed SDK at runtime**
> (`dotnet --version`) rather than assuming a version; the target runtime is whatever the stack/ADRs
> say (.NET 10 LTS). Once code exists, the SAME docs become living docs that `/wrap` drift-checks
> (and never auto-edits).

## 0. Orient
- Project root = the git repo root containing the cwd. Work there.
- Read `CLAUDE.md`, any handoff/spec, and whatever manifests exist (`*.csproj`, `global.json`,
  `package.json`) to pull the derivable facts. **Verify** cheap-to-check facts (installed SDK, file
  existence) rather than asserting them.
- Note which docs already exist (skip those) and, for the ones you'll create, jot the judgment each
  needs — that becomes the clarify list in step 2.

## 1. Create the tree (skip anything that already exists)
Root holds the browse-first project files (a hiring manager sees the root first). Only
docs/notes/logs live under `wiki/`.
```
<repo>/
  CLAUDE.md                # project instructions (skip if present)        [committed]
  README.md                # what this is + how to navigate the repo       [committed]
  CONTRIBUTING.md          # how to work in this repo + the doc workflow    [committed]
  specs/                   # feature specs (one file per feature)          [committed]
    README.md
  tasks/
    lessons.md             # running lessons-learned log (skip if present)  [committed]
  .github/                 # leave for CI/PR templates (create dir only)    [committed]
  wiki/
    README.md              # wiki index / TOC                              [committed]
    docs/                                                                   [committed]
      PRD.md  STACK.md  ARCHITECTURE.md  DATA-MODEL.md  DATA-FLOW.md
      API.md  RUNBOOK.md  GLOSSARY.md
      decisions/
        0000-template.md
        0001-record-architecture-decisions.md
    notes/                 # evergreen project knowledge                    [committed]
      README.md
    logs/                  # session logs YYYY-MM-DD.md — COMMITTED here    [committed]
      README.md
```
> [!note] This is a PUBLIC repo. `wiki/logs/` is **tracked** (the workflow is the point —
> committing the journal makes the agentic process visible). There is **no `wiki/raw/`** — no
> half-baked capture inbox belongs in a public portfolio repo. `wiki/docs/PATTERNS.md` from the
> harness original is intentionally **omitted** for this slice; conventions live in `CLAUDE.md` and
> ARCHITECTURE's cross-cutting-concerns section. Say so in the report.

## 2. Clarify the judgment (before drafting)
For each doc you're about to create, resolve the judgment it needs by **asking the human** — these
are the questions the docs can't be derived from facts alone. Ask, don't guess.
- **Batch by doc, in dependency order**, and work blocking docs first (PRD → STACK → ARCHITECTURE →
  DATA-MODEL → DATA-FLOW → API, then RUNBOOK / GLOSSARY / ADR-0001 / root files). Use
  `AskUserQuestion` with concrete options when the choice is enumerable; ask in prose when it's
  open-ended.
- The judgment to gather per doc:
  - **README** — the one-sentence positioning (what makes this slice worth a hiring manager's time).
  - **CONTRIBUTING** — branch/PR conventions + the definition of done.
  - **PRD** — the Problem statement; Goals + Success metrics ("good enough to demo," measurably); the
    in-scope boundary (which capabilities are IN the slice vs. merely named).
  - **STACK** — the **why** for each row (why this choice over the alternatives).
  - **ARCHITECTURE** — the layering decision (how the modules map to layers + why); cross-cutting
    concerns (config, error handling, where citations are enforced, the C#/TS conventions).
  - **DATA-MODEL** — entity relationships + cardinalities; confirm/adjust the field set + types; the
    invariants (e.g. must every ANSWER carry ≥1 CITATION?).
  - **DATA-FLOW** — the message arrows + ordering, including empty-result / error paths and exactly
    where/how citations attach.
  - **API** — each endpoint's route, request/response schema, and error model (this is the contract
    the scaffold will implement).
  - **RUNBOOK** — prerequisites + teardown.
  - **GLOSSARY** — any land/title term whose precise meaning matters to a domain reader.
  - **ADR-0001** — the Decision + Consequences for *recording architecture decisions* (the meta-ADR).
- **Don't over-ask.** If a fact is derivable or a default is low-stakes, take it and flag it inline
  with `*(assumption: …)*`; the bar is *would getting this wrong change the design or mislead a
  reader?* For foundational docs most judgment IS material, so expect real questions — but keep them
  short, batched, and skipped for any doc that already exists.
- **Iterate** until you and the human share the same understanding, then draft.

## 3. Draft each doc in full — derivable facts + the clarified judgment
Write each doc completely: the derivable scaffold below + the human's step-2 answers, woven into
prose/diagrams. Flag only leftover low-stakes defaults inline as `*(assumption: …)*`.

### Root files
- **README.md** — one paragraph: an ASP.NET Core (.NET 10) Web API + React/TypeScript SPA that runs
  a RAG vertical slice over land/title documents (ingest PDF → extract structured fields → chunk →
  embed → in-memory cosine retrieval → answer **with citations**). Add a "Repo map" TOC linking the
  root files + every `wiki/` doc. Open with the clarified positioning sentence.
- **CONTRIBUTING.md** — seed the doc workflow as fact: docs are authored BEFORE code as design
  intent; `/wiki-init` scaffolds, `/spec` opens a feature spec, `/adr` records a decision, `/wrap`
  keeps docs/logs current after code lands; ADRs in `wiki/docs/decisions/`; code lives under
  `/backend` (ASP.NET Core) and `/frontend` (React/TS); commits carry **no** `Co-Authored-By` /
  "Generated with" trailer. Add the clarified branch/PR conventions + definition of done.
- **specs/README.md** — one line: one feature spec per file, named `NNNN-<slug>.md`; link them as
  they're written.
- **tasks/lessons.md** — skip if it exists. If absent, seed the `# Lessons` H1 + the exact format
  line `[date] | what went wrong | rule next time` and note **newest at the bottom**.
- **.github/** — create the directory only (leave CI/PR templates to the human).

### wiki/docs
- **PRD.md** — sections: Problem · Goals · Non-goals · Users/personas · Use-cases · Scope (in/out) ·
  Success metrics · Open questions.
  - Derivable: **Users/personas = landmen and title users.** **Out of scope (name, don't build),
    verbatim from CLAUDE.md:** VNet/Private Link · Azure AI Document Intelligence OCR tuning · Azure
    AI Search · auth/RBAC · observability stack.
  - Write the Problem, Goals + Success metrics, and in-scope boundary from the step-2 answers.
- **STACK.md** — a table `layer · choice · version · why`. Fill the **why** column from the step-2
  rationale; **verify versions** (don't assert what's installed). Derivable scaffold:

  | layer | choice | version | why |
  |---|---|---|---|
  | Backend runtime | ASP.NET Core Web API, modular monolith (`Ingestion`/`Extraction`/`Retrieval`/`Qa`), under `/backend` | .NET 10 LTS (verify via `dotnet --version`) | (from step 2) |
  | Frontend | React + TypeScript SPA, under `/frontend` | (pin from package.json once scaffolded) | (from step 2) |
  | Chat port | `IChatClient` → `FoundryChatClient` (Microsoft Foundry gateway, primary) / `AnthropicChatClient` (Anthropic API direct, fallback) — config-only via `ModelClient:ChatProvider` | default chat `claude-opus-4-8`; Sonnet 4.6 / Haiku 4.5 selectable per call | (from step 2) |
  | Embedding port | `IEmbeddingClient` → `LocalEmbeddingClient` (in-memory, slice default) / `FoundryEmbeddingClient` (Azure OpenAI `text-embedding-3-small` via Foundry, production path) — config-only via `ModelClient:EmbeddingProvider` | — | (from step 2) |
  | Vector store | in-memory cosine similarity over `float[]` (slice); **Azure AI Search = production path, out of scope to build** | — | (from step 2) |

- **ARCHITECTURE.md** — system context (Mermaid `flowchart`) · components · ports/adapters ·
  cross-cutting concerns · boundaries.
  - Derivable: the code splits into **`/backend`** (ASP.NET Core modular monolith — one process,
    modules by folder/namespace: **Ingestion · Extraction · Retrieval · Qa**) and **`/frontend`**
    (React/TS SPA). The two ports: **`IChatClient`** (`FoundryChatClient` primary / `AnthropicChatClient`
    fallback, config-only) and **`IEmbeddingClient`** (`LocalEmbeddingClient` slice default /
    `FoundryEmbeddingClient` prod), with the in-memory cosine vector store (Azure AI Search the
    out-of-scope prod path). Starter context diagram (label it design intent):
    ```mermaid
    flowchart LR
      SPA[React/TS SPA /frontend] --> API[ASP.NET Core API /backend]
      API --> Ingestion --> Extraction --> Retrieval --> Qa
      Qa -->|IChatClient| Chat[(Foundry / Anthropic)]
      Retrieval -->|IEmbeddingClient| Embed[(Local / Azure OpenAI)]
    ```
  - Write the layering decision and the cross-cutting-concerns section from the step-2 answers.
- **DATA-MODEL.md** — domain types + a Mermaid `erDiagram`. Derivable scaffold = the entity field
  lists below; draw the relationship edges + cardinalities and state the invariants from step 2.
  ```mermaid
  erDiagram
    DOCUMENT {
      string id
      string filename
    }
    EXTRACTION {
      string lessor
      string lessee
      string legalDescription
      string royalty
      date   effectiveDate
    }
    CHUNK {
      string id
      string documentId
      string text
    }
    EMBEDDING {
      string chunkId
      float  vector
    }
    ANSWER {
      string text
    }
    CITATION {
      string chunkId
    }
  ```
- **DATA-FLOW.md** — a Mermaid `sequenceDiagram` of the ingest→answer-with-citation flow. Derivable =
  the participant list below; add the message arrows/ordering, the empty-result / error paths, and
  where citations attach, from step 2.
  ```mermaid
  sequenceDiagram
    actor User as Landman/Title user
    participant SPA
    participant API
    participant Ingestion
    participant Extraction
    participant Retrieval
    participant Qa
  ```
- **API.md** — public contracts: endpoints, request/response shapes, error model.
  - Derivable intended surface (design intent): an ingest/upload endpoint and a query/answer endpoint
    returning an answer **with citations**; a single typed API client on the SPA wraps `fetch`.
  - Write each endpoint's route, request/response schema, and error model from step 2.
- **RUNBOOK.md** — Prerequisites · Install · Run · Test · Build · Env/secrets (names only, never
  values) · Teardown. Derivable commands from CLAUDE.md:
  - **Backend** (`/backend`): `dotnet build` · `dotnet test` · `dotnet run --project src/LandDoc.Api`
  - **Frontend** (`/frontend`): `npm install` · `npm run dev` · `npm test`
  - Secret/config **names only** (e.g. `ModelClient:ChatProvider`, `ModelClient:EmbeddingProvider`,
    Foundry/Anthropic keys, Azure OpenAI config; dev via `dotnet user-secrets` / env vars).
  - Write prerequisites + teardown from step 2; note the commands get revised once the scaffold exists.
- **GLOSSARY.md** — domain + project terms, one line each. Derivable: lessor, lessee, legal
  description, royalty, RAG, chunk, embedding, cosine similarity, citation, `IChatClient`,
  `IEmbeddingClient`, Foundry, Azure AI Search, prompt caching. Add/refine domain terms from step 2.
- **decisions/0000-template.md** — the Nygard ADR template: Title · Status · Context · Decision ·
  Consequences (empty bodies — it's a template).
- **decisions/0001-record-architecture-decisions.md** — **Status: Accepted** once the human accepts
  at the gate (else Proposed), Date: today (ISO yyyy-mm-dd). Context = the framing ("this repo needs
  a durable record of architecture decisions"). Write the **Decision** and **Consequences** from the
  step-2 answer (this is the one meta-ADR `/wiki-init` drafts; all later ADRs go through `/adr`).

### wiki/notes & wiki/logs
- **notes/README.md** — one line: evergreen project knowledge, one topic per file, cross-linked with
  `[[wikilinks]]`, accrued by `/wrap`.
- **logs/README.md** — one line: committed session logs, one file per day `YYYY-MM-DD.md`, appended
  by `/wrap`; the journal is public here because the workflow is the deliverable.

## 4. Accept gate
- Present the drafted docs for the human to **accept** — `AskUserQuestion`: *Accept* / *Revise* /
  *Keep as draft*. Gate per doc or per logical group (root files · PRD/STACK · ARCHITECTURE/DATA-MODEL/
  DATA-FLOW · API/RUNBOOK/GLOSSARY · ADR-0001) — don't make the human ratify one giant blob.
  - **Accept** → write the file(s) and (for ADR-0001) set Status `Accepted`.
  - **Revise** → adjust per feedback and re-ask. Don't finalize until accepted.
  - **Keep as draft** → leave ADR-0001 `Proposed` / note the doc as unratified, and report it.
- Never clobber an existing file; only the missing ones go through this gate.

## 5. .gitignore — verify only (it already covers the artifacts)
- The repo `.gitignore` already excludes build + secret artifacts (`bin/`, `obj/`, `node_modules/`,
  `dist/`, `build/`, `*.env`, `.env.local`, `appsettings.*.local.json`, `secrets.json`). **Verify**
  it still excludes those and that it excludes **NO `wiki/` path** (logs are committed). Only append
  a missing artifact line if one is genuinely absent; never duplicate, never add a `wiki/` exclusion.

## 6. Report
- List **created vs. skipped** (path-level), the doc count, and any `.gitignore` change (expect: none).
- Note PATTERNS.md was intentionally omitted vs. the harness original.
- Flag any **significant architecture decision** drafted into the docs that should be formalized as its
  own ADR via `/adr` (e.g. the layering, the model-port split, the vector-store choice) — wiki-init
  drafts only the meta-ADR 0001; the rest are recorded with `/adr`.
- End with exactly: **"Now record the architecture decisions with /adr, open the first spec with /spec, then build under the tdd skill and run /wrap."**
