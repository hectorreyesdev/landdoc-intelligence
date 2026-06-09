namespace LandDoc.Api.Model;

/// <summary>
/// Per-deployment price for the computed cost estimate (spec 0009 / ADR-0020): USD per 1,000 tokens, input
/// and output separately. <b>Non-secret</b> config, bound from the <c>Pricing</c> section keyed by deployment
/// name. Cost is an estimate (tokens × price), not the Azure invoice.
/// </summary>
public sealed class DeploymentPrice
{
    /// <summary>USD per 1,000 prompt (input) tokens.</summary>
    public decimal InputPer1K { get; set; }

    /// <summary>USD per 1,000 completion (output) tokens.</summary>
    public decimal OutputPer1K { get; set; }
}
