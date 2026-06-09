namespace LandDoc.Evals.Core;

/// <summary>
/// One answer-quality eval case (spec 0012): a <see cref="Question"/> asked through the real
/// <c>/ask</c> path, the <see cref="ExpectedAnswer"/> (golden reference, used by the correctness
/// judge), and <see cref="ExpectedSources"/> — the source document file name(s) whose chunks should be
/// cited (used by recall@k). For multi-document cases (cross-linked sample sets) more than one source
/// is expected. <see cref="Id"/> is a stable human label for the report.
/// <para>
/// <see cref="Category"/> (what the case exercises — single-doc lookup, multi-doc retrieval, distractor
/// precision, abstention) and <see cref="Instrument"/> (the document-type label, for single-doc cases)
/// are optional reporting metadata: they let the snapshot/SPA group cases without affecting scoring.
/// </para>
/// </summary>
public sealed record EvalCase(
    string Id,
    string Question,
    string ExpectedAnswer,
    IReadOnlyList<string> ExpectedSources,
    string Category = "",
    string Instrument = "");
