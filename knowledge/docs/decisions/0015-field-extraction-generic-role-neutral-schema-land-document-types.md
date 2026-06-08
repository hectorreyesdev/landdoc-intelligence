# 0015. Field extraction — generic role-neutral schema across land-document types

- Status: Accepted
- Date: 2026-06-08

## Context
Field extraction is spec 0001's `IChatClient.ExtractFieldsAsync(string documentText)` — specced
OGL-shaped (lessor / lessee / royalty / legal description / key dates) and **never implemented**: both
`AzureOpenAIChatClient` and `AnthropicChatClient` throw `NotImplementedException`, and ingest already
degrades **best-effort** to empty fields on failure
([[knowledge/docs/specs/0001-document-ingestion-write-path]], the best-effort amendment).

The slice corpus is **36 documents across 23 distinct instrument types** (`samples/manifest.json`): 14
are Oil & Gas Leases; the other 22 types appear **once each** — Memorandum of Lease; Mineral / Royalty /
General-Warranty / Quitclaim Deeds; Division Order + Division-Order / Drilling Title Opinions; Affidavit
of Heirship; Order Admitting Will to Probate; Assignment-Bill-of-Sale; Joint-Operating / Farmout / AMI
Agreements; Pooling Order; Release / Ratification of Lease; Surface-Use Agreement; ROW / Pipeline
Easement; Grazing Lease; Lease Amendment; and an Authority for Expenditure (AFE). A single OGL schema
**mislabels** the non-leases — a deed has a Grantor/Grantee not a Lessor/Lessee; an AFE has neither.

Three ways to handle that breadth:
- **(a) Per-type registry** — 23 hand-authored schemas. Overkill: 22 of 23 types are n=1 in the corpus.
- **(b) Free-form LLM extraction** — flexible but **nondeterministic**: field names drift run-to-run, the
  same flakiness class we are actively removing from the Q&A path (ADR-0013 / ADR-0014).
- **(c) Generic role-neutral schema** — one code-defined structure the model fills.

Two facts make (c) cheap: the domain model
`ExtractedField(string Name, string Value, Guid? SourceChunkId)` is **already field-agnostic**, and the
sample manifest already records parties role-neutrally (`lessor_or_grantor` / `lessee_or_grantee`)
alongside `doc_type`, `effective_date`, `legal_description`, `county`, `state`, `acres`, `royalty`,
`bonus`, `primary_term` — i.e. the corpus schema *is* a generic role-neutral schema.

Relates to [[knowledge/docs/specs/0001-document-ingestion-write-path]] (defines `ExtractFieldsAsync` +
the field set this generalizes) and builds on
[[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]] (the live
`IChatClient` that runs the extraction call).

## Decision
We will implement `ExtractFieldsAsync` against a single **generic, role-neutral schema** in three tiers:
- **Universal core** (code-defined, always emitted): `DocumentType`; `Parties` as a list of
  `{role, name}` where the LLM labels the role (Lessor/Lessee, Grantor/Grantee, Operator,
  Assignor/Assignee, Affiant, Heirs…); `EffectiveDate`; `LegalDescription`; `County`; `State`.
- **Conditional economics** (emitted only when present): `Acres`, `Royalty`, `Bonus`, `PrimaryTerm`.
- **Open escape hatch**: `OtherNotableTerms: [{name, value}]` for type-specific terms (AFE amount,
  easement width, division-order decimal interest, pooling-unit acreage, depth limits…).

The **structure is code-defined**; the LLM only classifies `DocumentType`, labels party roles, fills
values, and populates the open slot. The call is **deterministic in shape** — structured outputs (a JSON
schema) at **temperature 0** — so the field *names* are stable run-to-run (values vary with the document,
as they should). The result **flattens to the existing `ExtractedField` list** (each party becomes one
field whose `Name` is its role; conditional / open terms become fields keyed by their name), so neither
the `ExtractedField` model nor the `IChatClient` port signature changes. This is **not** a per-type
registry and **not** free-form extraction. Best-effort degradation (spec 0001) is unchanged — a provider
failure still yields empty fields + a stored document. `SourceChunkId` stays **null**: extraction runs on
the full document text before chunking. *(assumption: the live extractor is the Azure OpenAI adapter per
ADR-0012; the Anthropic fallback may implement the same schema later.)*

## Consequences
- Works across all 23 sample types with **no per-type code**; the code-defined structure → a stable,
  testable shape the UI can render predictably.
- Values still vary with the document (correct); the **open slot** prevents silent loss of type-specific
  terms a fixed schema would drop.
- The `ExtractedField` model and the `IChatClient` port are **unchanged** — this implements an existing
  seam, not a contract change, so no port-changing spec is required. (Spec 0001's field-set *description*
  still needs a one-line amendment to drop the OGL-only framing.)
- Adding a new **core** field is a code change (acceptable at this scale); per-document **confidence
  scores** and **chunk-pinning** (`SourceChunkId`) are deferred.
- Production carry-forward: the same field shape maps onto **Azure AI Document Intelligence** prebuilt /
  custom models (managed extraction, same generic structure) — [[azure-rag-reference-architecture]].

## Links
- Relates to [[knowledge/docs/specs/0001-document-ingestion-write-path]] (the `ExtractFieldsAsync` seam +
  the field set this generalizes — needs a field-set amendment) and the sample corpus
  `samples/manifest.json`.
- Builds on [[knowledge/docs/decisions/0012-azure-openai-gpt-live-chat-adapter-per-provider-config]]
  (live `IChatClient`) · continues the determinism push of
  [[knowledge/docs/decisions/0013-azure-openai-text-embedding-3-small-live-slice-embedding-adapter]] /
  [[knowledge/docs/decisions/0014-surface-source-document-identity-in-ask-grounding-context]].
