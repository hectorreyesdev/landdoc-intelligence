using LandDoc.Evals.Core;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0006 — deterministic recall@k scoring (the framework-free half of the eval harness). These run
/// in the green suite: pure math, no model call, no keys.
/// </summary>
public sealed class RecallScoringTests
{
    [Fact]
    public void RecallAtK_AllExpectedRetrieved_IsOne()
    {
        var recall = RecallScoring.RecallAtK(["a", "b"], ["a", "b", "c"]);
        Assert.Equal(1.0, recall);
    }

    [Fact]
    public void RecallAtK_NoneRetrieved_IsZero()
    {
        var recall = RecallScoring.RecallAtK(["a", "b"], ["x", "y"]);
        Assert.Equal(0.0, recall);
    }

    [Fact]
    public void RecallAtK_PartialRetrieved_IsFraction()
    {
        // 1 of 2 expected present → 0.5
        var recall = RecallScoring.RecallAtK(["a", "b"], ["a", "z"]);
        Assert.Equal(0.5, recall);
    }

    [Fact]
    public void RecallAtK_EmptyExpected_IsOne()
    {
        var recall = RecallScoring.RecallAtK(Array.Empty<string>(), ["a"]);
        Assert.Equal(1.0, recall);
    }

    [Fact]
    public void RecallAtK_CollapsesDuplicates()
    {
        // Expected {a,b} (deduped from 3 entries); retrieved has a twice → still 0.5 (only a found).
        var recall = RecallScoring.RecallAtK(["a", "a", "b"], ["a", "a"]);
        Assert.Equal(0.5, recall);
    }

    [Fact]
    public void RecallAtK_HonoursComparer()
    {
        var recall = RecallScoring.RecallAtK(["DOC-1"], ["doc-1"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1.0, recall);
    }

    [Fact]
    public void RecallAtK_WorksOverGuids()
    {
        var shared = Guid.NewGuid();
        var recall = RecallScoring.RecallAtK([shared], [shared, Guid.NewGuid()]);
        Assert.Equal(1.0, recall);
    }
}
