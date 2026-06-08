namespace LandDoc.Api.Model;

/// <summary>
/// Config for the Azure OpenAI chat adapter, bound from the <c>AzureOpenAI</c> section (ADR-0012).
/// <see cref="Endpoint"/> and <see cref="ApiKey"/> are per-environment secrets — supply via
/// <c>dotnet user-secrets</c> / environment (<c>AzureOpenAI__*</c>) or managed identity in hosting,
/// never <c>appsettings.*</c>. <see cref="Deployment"/> is the Azure deployment name (the SDK
/// <c>deploymentName</c>), not the underlying model id.
/// </summary>
public sealed class AzureOpenAIOptions
{
    /// <summary>AOAI data-plane endpoint (the <c>.openai.azure.com</c> form — AZURE-CONFIG §4). From user-secrets; never committed.</summary>
    public string? Endpoint { get; set; }

    /// <summary>AOAI API key. From <c>dotnet user-secrets</c> / <c>AzureOpenAI__ApiKey</c>; never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure deployment name (the SDK <c>deploymentName</c>), e.g. <c>gpt-5.4-mini</c>.</summary>
    public string? Deployment { get; set; }

    /// <summary>Optional API version to pin; when null the SDK default is used.</summary>
    public string? ApiVersion { get; set; }
}
