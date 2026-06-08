namespace LandDoc.Api.Model;

/// <summary>
/// QA-context DTO passed to the chat port (spec 0002, ADR-0002): carries passage text without pulling a
/// dependency on <c>Storage</c> types into the chat port — hexagonal ports principle.
/// </summary>
public sealed record QaPassage(Guid ChunkId, Guid DocumentId, string Text, string SourceName);
