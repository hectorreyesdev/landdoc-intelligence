using LandDoc.Evals.Core;

namespace LandDoc.Evals;

/// <summary>
/// Loads the eval question set (<c>Dataset/questions.json</c>, copied next to the test assembly) once,
/// via the green-suite-tested <see cref="EvalDataset.Parse"/> in <c>LandDoc.Evals.Core</c>. Used both as
/// the xUnit <c>MemberData</c> source (test discovery, by case id) and to look the full case up at run time.
/// </summary>
public static class EvalDatasetProvider
{
    private static readonly Lazy<IReadOnlyList<EvalCase>> CasesLazy = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Dataset", "questions.json");
        return EvalDataset.Parse(File.ReadAllText(path));
    });

    /// <summary>All eval cases from the dataset.</summary>
    public static IReadOnlyList<EvalCase> Cases => CasesLazy.Value;

    /// <summary>Look up a single case by its stable id.</summary>
    public static EvalCase ById(string id) =>
        Cases.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No eval case with id '{id}' in the dataset.");
}
