---
description: One-time idempotent scaffold of the landdoc-intelligence doc tree (root docs + wiki/) — seeds derivable facts only, leaves every judgment as an AUTHOR marker, never clobbers.
argument-hint: "(no arg) — scaffold the doc tree once; re-run anytime to fill only what's missing"
---

# wiki-init — scaffold the landdoc-intelligence docs

Scaffold the documentation tree for THIS repo: root-level project files plus a `wiki/`
(living docs + evergreen notes + a COMMITTED session log). **Idempotent** — never clobber an
existing file; only create what's missing. Markdown + Mermaid (renders in Obsidian/GitHub).

> [!important] Author vs. enforce — the one rule that governs every step
> You SCAFFOLD structure and SEED only **derivable facts** (stack, versions, module names, the
> two model ports + their adapters, the out-of-scope list — pulled from the handoff / `CLAUDE.md`
> / manifests). You do **NOT** author **judgment**: product goals, scope decisions, architecture
> rationale, tradeoffs, ADR Decision/Consequences bodies. For every judgment section, drop a
> literal `> [!note] AUTHOR: <the question the human must answer>` callout and stop. **No
> exceptions** — even "obvious" meta-decisions (including the ADR-0001 decision body) are the
> human's to write. Reason: this is a public repo committed under the developer's name — he must
> be able to explain every line in an interview ("explain it in a meeting" gate). Auto-authored
> design docs are the over-vibing trap this whole repo exists to refute.

> [!important] Pre-scaffold reality — these docs are DESIGN INTENT right now
> There is **no app code yet** (only .NET 9 installed; .NET 10 + the scaffold come AFTER these
> docs). So ARCHITECTURE / DATA-MODEL / DATA-FLOW / API are authored NOW, by the human, as the
> **design the scaffold will implement** — not as descriptions of existing code. This command
> seeds their structure + known stack facts; the human fills the design choices. Once code
> exists, the SAME docs become living docs that `/wrap` drift-checks (and never auto-edits).

## 0. Orient
- Project root = the git repo root containing the cwd. Work there.
- Read `CLAUDE.md`, any handoff/spec, and whatever manifests exist (`*.csproj`, `global.json`,
  `package.json`) to pull the derivable facts. Seed each doc from those FACTS only; leave an
  `> [!note] AUTHOR:` callout everywhere a fact isn't available because it's a judgment call.

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
> harness original is intentionally **omitted** for this 3-day slice; conventions live in
> `CLAUDE.md` and ARCHITECTURE's cross-cutting-concerns section. Say so in the report.

## 2. Seed each doc — DERIVABLE facts now, AUTHOR markers for judgment

### Root files
- **README.md** — one paragraph: an ASP.NET Core (.NET 10) Web API + React/TypeScript SPA that
  runs a RAG vertical slice over land/title documents (ingest PDF → extract structured fields →
  chunk → embed → in-memory cosine retrieval → answer **with citations**). Add a "Repo map"
  TOC linking the root files + every `wiki/` doc. Mark the elevator pitch judgment:
  `> [!note] AUTHOR: one-sentence positioning — what makes this slice worth a hiring manager's time?`
- **CONTRIBUTING.md** — seed the doc workflow as fact: docs are authored BEFORE code as design
  intent; `/wiki-init` scaffolds, `/spec` opens a feature spec, `/adr` records a decision, `/wrap`
  keeps docs/logs current after code lands; ADRs in `wiki/docs/decisions/`; code lives under
  `/backend` (ASP.NET Core) and `/frontend` (React/TS); commits carry **no** `Co-Authored-By` /
  "Generated with" trailer.
  `> [!note] AUTHOR: branch/PR conventions and the definition of done for this repo.`
- **specs/README.md** — one line: one feature spec per file, named `NNNN-<slug>.md`; link them as
  they're written.
- **tasks/lessons.md** — skip if it exists (it does). If absent, seed the `# Lessons` H1 + the
  exact format line `[date] | what went wrong | rule next time` and note **newest at the bottom**
  (match the existing file; do NOT invert to newest-first).
- **.github/** — create the directory only (leave CI/PR templates to the human).

### wiki/docs
- **PRD.md** — sections: Problem · Goals · Non-goals · Users/personas · Use-cases · Scope
  (in/out) · Success metrics · Open questions.
  - SEED (fact): **Users/personas = landmen and title users.** **Out of scope (name, don't
    build), verbatim from CLAUDE.md:** VNet/Private Link · Azure AI Document Intelligence OCR
    tuning · Azure AI Search · auth/RBAC · observability stack.
  - `> [!note] AUTHOR: the Problem statement — what pain does this slice remove for landmen/title users?`
  - `> [!note] AUTHOR: Goals + Success metrics — what does "good enough to demo" mean, measurably?`
  - `> [!note] AUTHOR: in-scope boundary — which capabilities are IN the 3-day slice (vs. merely named)?`
- **STACK.md** — a table `layer · choice · version · why`. SEED the rows you can derive; leave
  the **why** column as an AUTHOR prompt per row (the rationale is judgment):

  | layer | choice | version | why |
  |---|---|---|---|
  | Backend runtime | ASP.NET Core Web API, modular monolith (`Ingestion`/`Extraction`/`Retrieval`/`Qa`), under `/backend` | .NET 10 LTS (.NET 9 installed; 10 post-design) | AUTHOR: |
  | Frontend | React + TypeScript SPA, under `/frontend` | (pin from package.json once scaffolded) | AUTHOR: |
  | Chat port | `IChatClient` → `FoundryChatClient` (Microsoft Foundry gateway, primary) / `AnthropicChatClient` (Anthropic API direct, fallback) — config-only via `ModelClient:ChatProvider` | default chat `claude-opus-4-8`; Sonnet 4.6 / Haiku 4.5 selectable per call | AUTHOR: |
  | Embedding port | `IEmbeddingClient` → `LocalEmbeddingClient` (in-memory, slice default) / `FoundryEmbeddingClient` (Azure OpenAI `text-embedding-3-small` via Foundry, production path) — config-only via `ModelClient:EmbeddingProvider` | — | AUTHOR: |
  | Vector store | in-memory cosine similarity over `float[]` (slice); **Azure AI Search = production path, out of scope to build** | — | AUTHOR: |

  `> [!note] AUTHOR: fill the "why" for each row — why this choice over the alternatives?`
- **ARCHITECTURE.md** — system context (Mermaid `flowchart`) · components · ports/adapters ·
  cross-cutting concerns · boundaries.
  - SEED (fact): the code splits into **`/backend`** (ASP.NET Core modular monolith — one process,
    modules by folder/namespace: **Ingestion · Extraction · Retrieval · Qa**) and **`/frontend`**
    (React/TS SPA). The two ports: **`IChatClient`** (`FoundryChatClient` primary / `AnthropicChatClient`
    fallback, config-only) and **`IEmbeddingClient`** (`LocalEmbeddingClient` slice default /
    `FoundryEmbeddingClient` prod), with the in-memory cosine vector store (Azure AI Search the
    out-of-scope prod path).
  - Seed a starter context diagram and label it design intent:
    ```mermaid
    flowchart LR
      SPA[React/TS SPA /frontend] --> API[ASP.NET Core API /backend]
      API --> Ingestion --> Extraction --> Retrieval --> Qa
      Qa -->|IChatClient| Chat[(Foundry / Anthropic)]
      Retrieval -->|IEmbeddingClient| Embed[(Local / Azure OpenAI)]
    ```
  - `> [!note] AUTHOR: the layering decision — how do these modules map to layers (hexagonal? service+ports?), and why?`
  - `> [!note] AUTHOR: cross-cutting concerns — config (model IDs in config, never hardcoded), error handling, where citations are enforced, the C#/TS conventions from CLAUDE.md.`
- **DATA-MODEL.md** — domain types + a Mermaid `erDiagram`. SEED the entity **field lists** (those
  are derivable from the handoff); do **NOT** pre-draw cardinality edges — relationships are
  design judgment. Put the AUTHOR prompt BEFORE the human draws edges, then seed bare entities:
  - `> [!note] AUTHOR: draw the relationships + cardinalities yourself (e.g. is EXTRACTION 1:1 with DOCUMENT? must every ANSWER carry ≥1 CITATION?). The boxes below are the derivable field lists; the edges between them are your call.`
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
  - `> [!note] AUTHOR: confirm/adjust the field set + types, then add the relationship lines and invariants once you've decided them.`
- **DATA-FLOW.md** — a Mermaid `sequenceDiagram` of the ingest→answer-with-citation flow. SEED the
  **participant list** (derivable); leave the message arrows/ordering to the human — who-calls-whom
  and where citations attach is design intent. Put the AUTHOR prompt BEFORE the diagram:
  - `> [!note] AUTHOR: draw the message arrows + ordering yourself, including the empty-result / error paths and exactly where/how citations are attached. The participants below are derivable; the sequence between them is your design.`
  ```mermaid
  sequenceDiagram
    actor User as Landman/Title user
    participant SPA
    participant API
    participant Ingestion
    participant Extraction
    participant Retrieval
    participant Qa
    %% AUTHOR: add the messages between participants (ingest, extract, chunk, embed, retrieve top-k, answer-with-citations)
  ```
- **API.md** — public contracts: endpoints, request/response shapes, error model.
  - SEED (fact) the intended surface as design intent: an ingest/upload endpoint and a
    query/answer endpoint returning an answer **with citations**; a single typed API client on the
    SPA side wraps `fetch`.
  - `> [!note] AUTHOR: define each endpoint's route, request/response schema, and error model — no app code exists yet, so this is the contract the scaffold will implement.`
- **RUNBOOK.md** — Prerequisites · Install · Run · Test · Build · Env/secrets (names only,
  never values) · Teardown. SEED the DERIVABLE commands from CLAUDE.md as facts:
  - **Backend** (`/backend`): `dotnet build` · `dotnet test` · `dotnet run --project src/LandDoc.Api`
  - **Frontend** (`/frontend`): `npm install` · `npm run dev` · `npm test`
  - SEED secret/config NAMES only (e.g. `ModelClient:ChatProvider`, `ModelClient:EmbeddingProvider`,
    Foundry/Anthropic keys, Azure OpenAI config; dev via `dotnet user-secrets` / env vars) — names,
    never values.
  - `> [!note] AUTHOR: prerequisites + teardown, and revise the commands above once the /backend + /frontend scaffold exists (CLAUDE.md flags them as not-yet-scaffolded).`
- **GLOSSARY.md** — domain + project terms, one line each. SEED the derivable ones: lessor,
  lessee, legal description, royalty, RAG, chunk, embedding, cosine similarity, citation,
  `IChatClient`, `IEmbeddingClient`, Foundry, Azure AI Search, prompt caching.
  - `> [!note] AUTHOR: add/refine any land/title term whose precise meaning matters to a domain reader.`
- **decisions/0000-template.md** — the Nygard ADR template: Title · Status · Context ·
  Decision · Consequences (empty bodies — it's a template).
- **decisions/0001-record-architecture-decisions.md** — seed ONLY the structure, **Status:
  Proposed**, Date: today (ISO yyyy-mm-dd). Context = derivable framing ("this repo needs a
  durable record of architecture decisions"). Leave **Decision** and **Consequences** as
  `> [!note] AUTHOR:` prompts exactly like every other ADR — the human writes the decision body
  and flips it to Accepted. No carve-out, no auto-filled bodies.

### wiki/notes & wiki/logs
- **notes/README.md** — one line: evergreen project knowledge, one topic per file, cross-linked
  with `[[wikilinks]]`, accrued by `/wrap`.
- **logs/README.md** — one line: committed session logs, one file per day `YYYY-MM-DD.md`,
  appended by `/wrap`; the journal is public here because the workflow is the deliverable.

## 3. .gitignore — verify only (it already covers the artifacts)
- The repo `.gitignore` already excludes build + secret artifacts (`bin/`, `obj/`, `node_modules/`,
  `dist/`, `build/`, `*.env`, `.env.local`, `appsettings.*.local.json`, `secrets.json`). **Verify**
  it still excludes those and that it excludes **NO `wiki/` path** (logs are committed). Only append
  a missing artifact line if one is genuinely absent; never duplicate, never add a `wiki/` exclusion.

## 4. Report
- List **created vs. skipped** (path-level), the doc count, and any `.gitignore` change (expect: none).
- Note PATTERNS.md was intentionally omitted vs. the harness original.
- **Prioritize the AUTHOR load for the deadline:** mark which sections are *blocking-for-scaffold*
  (PRD Problem/Goals/Scope · ARCHITECTURE layering · DATA-MODEL relationships · DATA-FLOW sequence ·
  API contracts) vs. *fill-later* (RUNBOOK prerequisites/teardown · GLOSSARY refinements) so day 1
  isn't spent on GLOSSARY lines.
- End with exactly: **"Now author the blocking AUTHOR: sections, record the first ADRs with /adr, open the first spec with /spec, then run /wrap."**
