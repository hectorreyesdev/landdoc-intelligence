namespace LandDoc.Api.Model;

/// <summary>
/// Config for the Anthropic-direct chat adapter — the config-swap fallback (ADR-0012) — bound from the
/// <c>Anthropic</c> section. <see cref="ApiKey"/> is a secret: <c>dotnet user-secrets</c> /
/// <c>Anthropic__ApiKey</c> only, never committed.
/// </summary>
public sealed class AnthropicOptions
{
    /// <summary>Anthropic API key. From <c>dotnet user-secrets</c> / <c>Anthropic__ApiKey</c>; never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Chat model id. Default <c>claude-opus-4-8</c>.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Optional base URL override (e.g. a gateway); when null the SDK default (api.anthropic.com) is used.</summary>
    public string? BaseUrl { get; set; }
}
