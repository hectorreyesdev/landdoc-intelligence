namespace LandDoc.Api.Model;

/// <summary>
/// Config for the Azure Blob document-store adapter, bound from the <c>Blob</c> section (ADR-0018).
/// Auth is managed-identity-preferred (ADR-0016): if <see cref="ServiceUri"/> is set the adapter uses
/// <c>DefaultAzureCredential</c> (passwordless — the hosting path); otherwise it falls back to
/// <see cref="ConnectionString"/> (the already-provisioned <c>Blob--ConnectionString</c> Key Vault secret,
/// and Azurite/local dev). Secrets come from <c>dotnet user-secrets</c> / Key Vault, never
/// <c>appsettings.*</c>.
/// </summary>
public sealed class BlobOptions
{
    /// <summary>Blob service endpoint (the <c>https://&lt;account&gt;.blob.core.windows.net</c> form). When set, auth is via managed identity.</summary>
    public string? ServiceUri { get; set; }

    /// <summary>Storage account connection string. Fallback for local/dev (Azurite) or when no <see cref="ServiceUri"/> is configured. From user-secrets / Key Vault; never committed.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Container that holds the document blobs. Defaults to <c>documents</c>.</summary>
    public string ContainerName { get; set; } = "documents";
}
