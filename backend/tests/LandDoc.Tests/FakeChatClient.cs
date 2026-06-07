using LandDoc.Api.Model;
using LandDoc.Api.Storage;

namespace LandDoc.Tests;

/// <summary>
/// Deterministic, offline <see cref="IChatClient"/> for tests: returns canned extracted fields covering
/// all five field types (lessor, lessee, legal description, royalty, a date) plus a canned answer. No
/// network, no provider — keeps the acceptance test reproducible.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(string documentText, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ExtractedField> fields =
        [
            new ExtractedField("Lessor", "John Q. Landowner", null),
            new ExtractedField("Lessee", "Acme Minerals LLC", null),
            new ExtractedField("LegalDescription", "Section 14, Block 2, T-1-N, Permian County", null),
            new ExtractedField("Royalty", "3/16", null),
            new ExtractedField("EffectiveDate", "2026-01-15", null),
        ];

        return Task.FromResult(fields);
    }

    public Task<string> AnswerAsync(string question, IReadOnlyList<Chunk> context, CancellationToken cancellationToken = default)
        => Task.FromResult("The lessee is Acme Minerals LLC.");
}
