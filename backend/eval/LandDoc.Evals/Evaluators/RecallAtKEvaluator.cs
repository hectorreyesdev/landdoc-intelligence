using LandDoc.Evals.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace LandDoc.Evals.Evaluators;

/// <summary>
/// The per-case inputs the deterministic <see cref="RecallAtKEvaluator"/> needs, passed to the
/// evaluation run as an <see cref="EvaluationContext"/>: the eval case's expected source document
/// <see cref="ExpectedSources"/> (file names) and the <see cref="CitedSources"/> actually returned in the
/// <c>/ask</c> response's citations (their <c>Citation.Source</c> values). Both are file names; recall@k
/// matches them case-insensitively.
/// </summary>
public sealed class RecallAtKContext : EvaluationContext
{
    public const string ContextName = "Recall@K Inputs";

    public IReadOnlyList<string> ExpectedSources { get; }
    public IReadOnlyList<string> CitedSources { get; }

    public RecallAtKContext(IReadOnlyList<string> expectedSources, IReadOnlyList<string> citedSources)
        : base(
            ContextName,
            $"Expected sources: [{string.Join(", ", expectedSources)}]; " +
            $"Cited sources: [{string.Join(", ", citedSources)}]")
    {
        ExpectedSources = expectedSources;
        CitedSources = citedSources;
    }
}

/// <summary>
/// Custom, deterministic retrieval recall@k evaluator (spec 0012, ADR-0021). Wraps the pure
/// <see cref="RecallScoring.RecallAtK{T}"/> from <c>LandDoc.Evals.Core</c> (unit-tested in the green
/// suite) — it makes <b>no</b> model call. For each case it scores the share of the case's expected
/// source documents that appear in the <c>/ask</c> answer's citations, matching <c>Citation.Source</c>
/// file names case-insensitively. Reads its inputs from the <see cref="RecallAtKContext"/> supplied in
/// <c>additionalContext</c>; returns a <see cref="NumericMetric"/> in [0, 1].
/// </summary>
public sealed class RecallAtKEvaluator : IEvaluator
{
    public const string MetricName = "Recall@K";

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [MetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var context = additionalContext?.OfType<RecallAtKContext>().FirstOrDefault();

        NumericMetric metric;
        if (context is null)
        {
            metric = new NumericMetric(
                MetricName,
                value: null,
                reason: $"No {nameof(RecallAtKContext)} was supplied, so recall@k could not be computed.");
        }
        else
        {
            var recall = RecallScoring.RecallAtK(
                context.ExpectedSources, context.CitedSources, StringComparer.OrdinalIgnoreCase);

            metric = new NumericMetric(
                MetricName,
                recall,
                reason: context.ExpectedSources.Count == 0
                    ? "No source documents were required (e.g. an absent-answer case), so recall is vacuously 1.0."
                    : $"{recall:P0} of the expected source document(s) appeared in the answer's citations.");
        }

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
