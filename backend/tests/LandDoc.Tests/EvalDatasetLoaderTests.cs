using LandDoc.Evals.Core;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0009 — the eval dataset loader (pure data, no model call). Green-suite tests for parsing +
/// validation; the loader rejects malformed/incomplete cases early.
/// </summary>
public sealed class EvalDatasetLoaderTests
{
    private const string ValidJson = """
    [
      {
        "id": "midland-royalty",
        "question": "What is the royalty in the Midland County lease?",
        "expectedAnswer": "one-fourth (1/4)",
        "expectedSources": ["01-ogl-midland-tx.pdf"]
      },
      {
        "id": "henderson-legal",
        "question": "What is the legal description of the Henderson tract?",
        "expectedAnswer": "Section 30, Block C-24, PSL Survey, Abstract No. 612",
        "expectedSources": ["22-title-opinion-loving-tx.pdf", "27-affidavit-heirship-loving-tx.pdf"]
      }
    ]
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsCases()
    {
        var cases = EvalDataset.Parse(ValidJson);

        Assert.Equal(2, cases.Count);
        var henderson = cases.Single(c => c.Id == "henderson-legal");
        Assert.Equal("Section 30, Block C-24, PSL Survey, Abstract No. 612", henderson.ExpectedAnswer);
        Assert.Equal(2, henderson.ExpectedSources.Count);
        Assert.Contains("22-title-opinion-loving-tx.pdf", henderson.ExpectedSources);
    }

    [Fact]
    public void Parse_EmptyArray_Throws()
    {
        Assert.Throws<FormatException>(() => EvalDataset.Parse("[]"));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.Throws<FormatException>(() => EvalDataset.Parse("{ not an array"));
    }

    [Fact]
    public void Parse_CaseMissingQuestion_Throws()
    {
        const string json = """
        [{ "id": "x", "expectedAnswer": "a", "expectedSources": ["f.pdf"] }]
        """;
        var ex = Assert.Throws<FormatException>(() => EvalDataset.Parse(json));
        Assert.Contains("question", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_CaseWithNoExpectedSources_Throws()
    {
        const string json = """
        [{ "id": "x", "question": "q?", "expectedAnswer": "a", "expectedSources": [] }]
        """;
        Assert.Throws<FormatException>(() => EvalDataset.Parse(json));
    }

    [Fact]
    public void Parse_DuplicateIds_Throws()
    {
        const string json = """
        [
          { "id": "dup", "question": "q1?", "expectedAnswer": "a", "expectedSources": ["f.pdf"] },
          { "id": "dup", "question": "q2?", "expectedAnswer": "b", "expectedSources": ["g.pdf"] }
        ]
        """;
        Assert.Throws<FormatException>(() => EvalDataset.Parse(json));
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => EvalDataset.LoadAsync("/no/such/eval-dataset.json"));
    }
}
