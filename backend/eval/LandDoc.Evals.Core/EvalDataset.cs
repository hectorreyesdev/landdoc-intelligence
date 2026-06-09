using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandDoc.Evals.Core;

/// <summary>
/// Loads the eval question set (spec 0006) from a <c>questions.json</c> file. The dataset is pure data —
/// no model calls — so this loader lives in the dependency-free core and is unit-tested in the green
/// suite. Validates each case (non-blank question/answer, ≥1 expected source) and throws early on bad
/// input, matching the repo's validate-and-throw convention.
/// </summary>
public static class EvalDataset
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses and validates the eval cases from a JSON string.</summary>
    public static IReadOnlyList<EvalCase> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        List<CaseDto>? dtos;
        try
        {
            dtos = JsonSerializer.Deserialize<List<CaseDto>>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Eval dataset is not valid JSON.", ex);
        }

        if (dtos is null || dtos.Count == 0)
            throw new FormatException("Eval dataset is empty — at least one case is required.");

        var cases = new List<EvalCase>(dtos.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
                throw new FormatException("Eval case is missing 'id'.");
            if (!seenIds.Add(dto.Id))
                throw new FormatException($"Duplicate eval case id '{dto.Id}'.");
            if (string.IsNullOrWhiteSpace(dto.Question))
                throw new FormatException($"Eval case '{dto.Id}' is missing 'question'.");
            if (string.IsNullOrWhiteSpace(dto.ExpectedAnswer))
                throw new FormatException($"Eval case '{dto.Id}' is missing 'expectedAnswer'.");
            if (dto.ExpectedSources is null || dto.ExpectedSources.Count == 0 ||
                dto.ExpectedSources.Any(string.IsNullOrWhiteSpace))
                throw new FormatException(
                    $"Eval case '{dto.Id}' must list at least one non-blank expected source.");

            cases.Add(new EvalCase(
                dto.Id.Trim(),
                dto.Question.Trim(),
                dto.ExpectedAnswer.Trim(),
                dto.ExpectedSources.Select(s => s.Trim()).ToList()));
        }

        return cases;
    }

    /// <summary>Loads and validates the eval cases from a JSON file on disk.</summary>
    public static async Task<IReadOnlyList<EvalCase>> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Eval dataset not found at '{path}'.", path);

        var json = await File.ReadAllTextAsync(path, ct);
        return Parse(json);
    }

    private sealed class CaseDto
    {
        public string? Id { get; init; }
        public string? Question { get; init; }
        public string? ExpectedAnswer { get; init; }

        [JsonPropertyName("expectedSources")]
        public List<string>? ExpectedSources { get; init; }
    }
}
