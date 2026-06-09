namespace LandDoc.Api.Model;

/// <summary>
/// Config for the Azure Monitor usage source (ADR-0020), bound from the <c>Monitor</c> section.
/// <see cref="ResourceId"/> is the Foundry/Azure OpenAI resource id whose platform metrics back the usage
/// dashboard. It is <b>non-secret</b> (no Key Vault) — supply via <c>appsettings</c> or
/// <c>Monitor__ResourceId</c>. Auth is managed identity (<c>DefaultAzureCredential</c>), never a key.
/// </summary>
public sealed class MonitorOptions
{
    /// <summary>The Foundry resource id (e.g. <c>/subscriptions/…/providers/Microsoft.CognitiveServices/accounts/…</c>).</summary>
    public string? ResourceId { get; set; }
}
