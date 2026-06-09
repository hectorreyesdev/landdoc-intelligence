namespace LandDoc.Evals.Core;

/// <summary>
/// One answer-quality eval case (spec 0006): a <see cref="Question"/> asked through the real
/// <c>/ask</c> path, the <see cref="ExpectedAnswer"/> (golden reference, used by the correctness
/// judge), and <see cref="ExpectedSources"/> — the source document file name(s) whose chunks should be
/// cited (used by recall@k). For multi-document cases (cross-linked sample sets) more than one source
/// is expected. <see cref="Id"/> is a stable human label for the report.
/// </summary>
public sealed record EvalCase(
    string Id,
    string Question,
    string ExpectedAnswer,
    IReadOnlyList<string> ExpectedSources);
