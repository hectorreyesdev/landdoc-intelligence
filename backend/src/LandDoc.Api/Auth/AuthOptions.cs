namespace LandDoc.Api.Auth;

/// <summary>
/// Config for the single-user gate, bound from the <c>Auth</c> section (spec 0013, ADR-0022).
/// <c>Mode=none</c> (the default) leaves the app open — local dev, offline mode, and tests.
/// <c>Mode=easyauth</c> requires every request to carry the principal header Container Apps Easy Auth
/// injects, with the principal allowlisted. Object IDs are not secrets — plain config / env vars,
/// never Key Vault.
/// </summary>
public sealed class AuthOptions
{
    public const string ModeNone = "none";
    public const string ModeEasyAuth = "easyauth";

    /// <summary><c>none</c> (default) or <c>easyauth</c>. Any other value fails startup.</summary>
    public string Mode { get; set; } = ModeNone;

    /// <summary>Entra object IDs allowed through when <see cref="Mode"/> is <c>easyauth</c> (one entry today — the owner). Must be non-empty in that mode.</summary>
    public List<string> AllowedPrincipalIds { get; set; } = [];
}
