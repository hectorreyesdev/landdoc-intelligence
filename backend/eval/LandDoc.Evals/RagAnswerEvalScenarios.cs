using System.Net.Http.Json;
using LandDoc.Api.Qa;
using LandDoc.Evals.Evaluators;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Configuration;

namespace LandDoc.Evals;

/// <summary>
/// The answer-quality eval scenarios (spec 0009, ADR-0020). One xUnit theory per dataset case: ask the
/// real <c>/ask</c> endpoint, then score the answer on three metrics through a
/// <see cref="Microsoft.Extensions.AI.Evaluation.Reporting.ReportingConfiguration"/> backed by a disk
/// result store:
/// <list type="bullet">
///   <item><b>recall@k</b> — deterministic <see cref="RecallAtKEvaluator"/> over the case's expected
///   source file names vs the answer's <c>Citation.Source</c> values.</item>
///   <item><b>groundedness</b> — <see cref="GroundednessEvaluator"/>, grounding context = the concatenated
///   text of the answer's citations (only what the model was shown).</item>
///   <item><b>correctness</b> — <see cref="EquivalenceEvaluator"/> against the case's golden answer.</item>
/// </list>
/// <b>Report-only by default</b>: the run records scores and never fails on quality. Set
/// <c>Eval:Thresholds:Enabled=true</c> to turn on per-metric floors (read from <c>Eval:Thresholds:{metric}</c>)
/// that fail the run when a metric falls below its floor.
/// <para>Requires real secrets — see the project README; these scenarios cannot run offline.</para>
/// </summary>
public sealed class RagAnswerEvalScenarios : IClassFixture<EvalPipelineFixture>
{
    private const string AbsentCaseIdPrefix = "absent-";

    private readonly EvalPipelineFixture _fixture;

    public RagAnswerEvalScenarios(EvalPipelineFixture fixture) => _fixture = fixture;

    /// <summary>Test-discovery data source: one entry per case id (string — serializable for xUnit).</summary>
    public static IEnumerable<object[]> Cases() =>
        EvalDatasetProvider.Cases.Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Scores_answer_quality(string caseId)
    {
        var evalCase = EvalDatasetProvider.ById(caseId);

        // 1) Ask through the REAL /ask read path.
        var askResponse = await _fixture.Client.PostAsJsonAsync("/ask", new AskRequest(evalCase.Question));
        askResponse.EnsureSuccessStatusCode();
        var answer = await askResponse.Content.ReadFromJsonAsync<AskResponse>()
            ?? throw new InvalidOperationException($"[{caseId}] /ask returned an empty body.");

        // 2) Build the three evaluators' inputs from the answer.
        var citedSources = answer.Citations.Select(c => c.Source).ToList();
        var groundingContext = string.Join("\n\n", answer.Citations.Select(c => c.Text));

        // Absent-answer cases (no correct source in the corpus): recall is vacuous — feed empty expected
        // (RecallScoring: empty expected ⇒ 1.0, "nothing required was missed"). The sentinel source in the
        // dataset only satisfies the loader's >=1-source rule; it is intentionally ignored here.
        var isAbsentCase = evalCase.Id.StartsWith(AbsentCaseIdPrefix, StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<string> expectedForRecall = isAbsentCase ? [] : evalCase.ExpectedSources;

        var contexts = new EvaluationContext[]
        {
            new RecallAtKContext(expectedForRecall, citedSources),
            new GroundednessEvaluatorContext(groundingContext),
            new EquivalenceEvaluatorContext(evalCase.ExpectedAnswer),
        };

        var messages = new[] { new ChatMessage(ChatRole.User, evalCase.Question) };
        var modelResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Answer));

        // 3) Run all evaluators for this case; the disk result store feeds the `aieval` HTML report.
        await using var scenarioRun = await _fixture.Reporting.CreateScenarioRunAsync(evalCase.Id);
        var result = await scenarioRun.EvaluateAsync(messages, modelResponse, contexts);

        // 4) Record this case's scores for the SPA snapshot (spec 0011 — eval-summary.json).
        double? Metric(string name) =>
            result.Metrics.TryGetValue(name, out var m) && m is NumericMetric nm && nm.Value is double v
                ? Math.Round(v, 2)
                : null;
        var abstained = answer.Answer.Contains(
            "The answer is not found in the document(s)", StringComparison.OrdinalIgnoreCase);
        _fixture.Record(new EvalCaseSummary(
            evalCase.Id,
            Metric(RecallAtKEvaluator.MetricName),
            Metric("Groundedness"),
            Metric("Equivalence"),
            abstained));

        // 5) Gating: report-only by default; opt-in per-metric floors.
        var thresholdsEnabled = _fixture.Configuration.GetValue("Eval:Thresholds:Enabled", false);
        if (!thresholdsEnabled)
        {
            // Report-only: confirm the run produced metrics (wiring sanity), never gate on quality.
            Assert.NotEmpty(result.Metrics);
            return;
        }

        foreach (var metric in result.Metrics.Values.OfType<NumericMetric>())
        {
            var floor = _fixture.Configuration.GetValue<double?>($"Eval:Thresholds:{metric.Name}");
            if (floor is double minimum)
            {
                Assert.True(
                    metric.Value is double value && value >= minimum,
                    $"[{caseId}] {metric.Name} = {metric.Value?.ToString() ?? "null"} is below the configured " +
                    $"floor {minimum}. Reason: {metric.Reason}");
            }
        }
    }
}
