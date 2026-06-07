namespace LandDoc.Api.Model;

/// <summary>
/// A structured field the extractor pulls from a document (lessor, lessee, legal description, royalty,
/// key dates…). <see cref="SourceChunkId"/> is null when the model doesn't pin the field to a chunk.
/// </summary>
public sealed record ExtractedField(string Name, string Value, Guid? SourceChunkId);
