namespace LandDoc.Api.Model;

/// <summary>
/// Top-level model-access selectors, bound from the <c>ModelClient</c> config section. Provider
/// credentials live in their own per-provider sections (<c>AzureOpenAI:*</c>, <c>Anthropic:*</c> —
/// ADR-0012); this type only chooses which adapter to wire. No secrets live here.
/// </summary>
public sealed class ModelClientOptions
{
    /// <summary>Which chat adapter to activate: <c>azureopenai</c> (live slice default, ADR-0012) or <c>anthropic</c> (config-swap fallback).</summary>
    public string ChatProvider { get; init; } = "azureopenai";

    /// <summary>Embedding provider: <c>local</c> (slice default, ADR-0008) or <c>foundry</c>.</summary>
    public string EmbeddingProvider { get; init; } = "local";
}
