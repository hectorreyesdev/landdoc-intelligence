namespace LandDoc.Api.Model;

/// <summary>
/// Config options for the model-access adapters — bound from the <c>ModelClient</c> config section.
/// Credentials (ApiKey) must come from <c>dotnet user-secrets</c> or environment variables, never
/// from committed source or <c>appsettings.*</c> files.
/// </summary>
public sealed class ModelClientOptions
{
    /// <summary>Which chat adapter to activate: <c>anthropic</c> (slice default) or <c>foundry</c> (production primary).</summary>
    public string ChatProvider { get; init; } = "anthropic";

    /// <summary>Chat model id passed to the adapter. Default: <c>claude-opus-4-8</c>.</summary>
    public string Model { get; init; } = "claude-opus-4-8";

    /// <summary>API key for the active chat adapter. Set via <c>dotnet user-secrets</c> or environment variable — never committed.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Optional base URL override for the chat adapter endpoint. When empty the adapter uses its own
    /// default (e.g. <c>https://api.anthropic.com/</c> for Anthropic-direct). Populate to route to a
    /// Foundry gateway or other proxy without code changes.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Embedding provider: <c>local</c> (slice default) or <c>foundry</c>.</summary>
    public string EmbeddingProvider { get; init; } = "local";
}
