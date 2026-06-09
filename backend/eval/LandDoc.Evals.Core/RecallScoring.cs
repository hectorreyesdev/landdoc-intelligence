namespace LandDoc.Evals.Core;

/// <summary>
/// Deterministic retrieval recall@k scoring (spec 0009) — the share of expected items that appear in
/// the retrieved set. Pure and framework-free: the eval runner wraps this in a
/// <c>Microsoft.Extensions.AI.Evaluation.IEvaluator</c>, but the math is unit-tested here in the green
/// suite with no model call. Generic over the item type so it can score document ids (the real harness)
/// or strings (tests) alike.
/// </summary>
public static class RecallScoring
{
    /// <summary>
    /// Returns the fraction of distinct <paramref name="expected"/> items found in
    /// <paramref name="retrieved"/>, in [0, 1]. An empty <paramref name="expected"/> set scores 1.0
    /// (nothing was required, so nothing is missing). Duplicates on either side are collapsed.
    /// </summary>
    public static double RecallAtK<T>(
        IEnumerable<T> expected,
        IEnumerable<T> retrieved,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(retrieved);

        var expectedSet = new HashSet<T>(expected, comparer);
        if (expectedSet.Count == 0)
            return 1.0;

        var retrievedSet = new HashSet<T>(retrieved, comparer);
        var found = expectedSet.Count(retrievedSet.Contains);
        return (double)found / expectedSet.Count;
    }
}
