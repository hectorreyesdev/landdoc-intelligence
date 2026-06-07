namespace LandDoc.Api.Qa;

/// <summary>Request body for <c>POST /ask</c> (spec 0002).</summary>
public sealed record AskRequest(string Question);
