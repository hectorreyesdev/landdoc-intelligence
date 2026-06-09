# 0010 — RAG answer-quality tuning (retrieval depth + abstain prompt)

**Status:** Accepted

## What to build
Act on the misses the eval harness ([[knowledge/docs/specs/0009-rag-answer-quality-eval-harness]]) surfaced
on its first live run (2026-06-09). Two distinct failure modes, two targeted levers — measured against the
harness, not guessed:

1. **Retrieval/chunking miss** — `reeves-royalty` (9/40) and `reeves-term` (five years): the value-bearing
   chunk was **not** in the top-k, so the model could not answer. **Lever:** raise `Retrieval:TopK`
   (8 → 12) so more candidate chunks reach the answer prompt. Chunking parameters
   (`Chunking:MaxChars`/`Overlap`) are held in reserve — only revisit if a depth bump doesn't recover the
   value chunk.
2. **Generation over-abstention** — `henderson-acres-multi` (640 acres) and `mckenzie-royalty` (3/16): the
   answer **was** in the cited passages, yet the model returned the abstain string. **Lever:** soften the
   answer system prompt so it answers when the passages contain the answer (including when it must be
   combined across passages), and abstains **only** when the answer is genuinely absent.

This is a **tuning** change to the system-under-test, not a structural one. No ports, endpoints, or DTOs
change.

## Constraints
- **Retrieval depth:** `Retrieval:TopK` 8 → 12 in `appsettings.json` (the code default in
  `RetrievalOptions` stays 5). Config-only; no code change in the retrieval path.
- **Answer prompt:** edit the `SystemPrompt` in **both** chat adapters
  (`AzureOpenAIChatClient` — the live SUT — and `AnthropicChatClient`, kept in sync) to reduce
  over-abstention. **The exact abstain sentence `"The answer is not found in the document(s)."` MUST be
  preserved verbatim** — the cite-or-abstain invariant (API.md), the eval's absent-answer golden answers
  (spec 0009), and any downstream string checks depend on it.
- **No port / contract change:** `IChatClient`, `IEmbeddingClient`, `IVectorStore`, `/ask`, `/documents`
  are untouched. The `AnswerAsync` signature and the strict cite-or-error invariant are unchanged.
- **Green suite stays green + offline:** `dotnet test LandDoc.slnx` passes with no keys. The answer
  prompt is exercised only by the real adapters (tests use `FakeChatClient`), so prompt wording has no
  deterministic unit test; its effect is measured by the eval harness (the behavioral test for answer
  quality). TopK is covered by `ChunkRetrieverTests` (which sets its own `RetrievalOptions`).
- **Scope:** retrieval depth + answer prompt only. No re-chunking/re-embedding of the live index is
  required by this change (a `TopK` bump needs neither); a future chunking change would.

## How to verify
- **Green suite:** `dotnet test LandDoc.slnx` stays green offline (TopK bump doesn't change the
  parameterized `ChunkRetrieverTests`; no integration test asserts a fixed citation count).
- **Eval harness (live, real stack):** re-run `dotnet test backend/eval/LandDoc.Evals` against the prod
  stack and compare to the 2026-06-09 baseline (recall 0.96 · groundedness 4.67 · correctness 3.94):
  - `reeves-royalty` / `reeves-term`: the value chunk now appears in citations (recall stays 1.0 and the
    model answers → correctness rises from 1).
  - `henderson-acres-multi` / `mckenzie-royalty`: the model now answers from the present passages
    (correctness rises from 1; multi-doc recall may rise from 0.67 toward 1.0 with more depth).
  - **Guardrail:** the two `absent-*` cases still abstain with the exact string (correctness stays 5) —
    softening the prompt must not (re)introduce hallucination.
- **Iterate:** if a metric regresses or a miss persists, adjust the lever (e.g. chunking) and re-measure;
  the harness is the feedback loop.

## Links
- **Driven by:** [[knowledge/docs/specs/0009-rag-answer-quality-eval-harness]] (the harness that surfaced
  and will re-measure these misses) · [[knowledge/docs/decisions/0020-llm-eval-harness-and-judge-model]].
- **Touches:** the answer path of [[knowledge/docs/specs/0002-rag-qa-with-citations]] (prompt wording;
  the cite-or-abstain invariant is unchanged) and the retrieval seam of
  [[knowledge/docs/specs/0004-extract-retrieval-service]] (`Retrieval:TopK`).
- **Operations:** [[knowledge/docs/EVAL-HARNESS.md]] (how to run the re-measurement).
- **Docs to reconcile on merge:** none structural — `EVAL-HARNESS.md` §11 baseline gets a post-tuning
  comparison; no API/DATA-MODEL change.
