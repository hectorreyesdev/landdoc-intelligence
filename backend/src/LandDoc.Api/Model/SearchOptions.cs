namespace LandDoc.Api.Model;

/// <summary>
/// Config for the Azure AI Search vector-store adapter, bound from the <c>Search</c> section (ADR-0017).
/// <see cref="Endpoint"/> and <see cref="ApiKey"/> are per-environment secrets — supply via
/// <c>dotnet user-secrets</c> / environment (<c>Search__*</c>) or Key Vault (ADR-0016), never
/// <c>appsettings.*</c>.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>Azure AI Search data-plane endpoint (the <c>.search.windows.net</c> form). From user-secrets; never committed.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Azure AI Search admin key. From <c>dotnet user-secrets</c> / <c>Search__ApiKey</c>; never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Index name for chunks. Defaults to <c>landdoc-chunks</c>.</summary>
    public string IndexName { get; set; } = "landdoc-chunks";
}
