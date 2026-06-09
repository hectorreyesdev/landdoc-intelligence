namespace LandDoc.Evals;

/// <summary>
/// Compact, SPA-friendly summary of an eval run (spec 0011). Written to the result-store root on dispose,
/// then promoted into the frontend and committed as a dated snapshot the Dashboard renders. Serialized
/// camelCase so it imports directly into the TypeScript <c>EvalSummary</c> shape.
/// </summary>
public sealed record EvalSummary(
    string GeneratedAt,
    string JudgeModel,
    int CaseCount,
    EvalMeans Means,
    IReadOnlyList<EvalCaseSummary> Cases);

/// <summary>Per-metric means across the run (null if a metric never produced a value).</summary>
public sealed record EvalMeans(double? RecallAtK, double? Groundedness, double? Equivalence);

/// <summary>
/// One case's scores plus the demo-facing dataset text (question / expected answer / expected source
/// file names) so the snapshot is self-describing — the SPA can show the actual Q&amp;A behind each case
/// without shipping the backend dataset. <see cref="Abstained"/> is true when the answer was the exact
/// abstain string (the no-hallucination path).
/// </summary>
public sealed record EvalCaseSummary(
    string Id,
    string Question,
    string ExpectedAnswer,
    IReadOnlyList<string> ExpectedSources,
    double? RecallAtK,
    double? Groundedness,
    double? Equivalence,
    bool Abstained);
