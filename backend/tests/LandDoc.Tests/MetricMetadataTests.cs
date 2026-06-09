using LandDoc.Api.Storage;

namespace LandDoc.Tests;

/// <summary>
/// ADR-0020 / spec 0009 — guards the live-metrics regression where Azure returns split-dimension keys
/// lower-cased (<c>modeldeploymentname</c> / <c>statuscode</c>), so a case-sensitive lookup silently
/// dropped every series → zero tokens, empty per-deployment table, zero status buckets.
/// </summary>
public sealed class MetricMetadataTests
{
    [Fact]
    public void TryGetDimension_matchesAzuresLowercasedKey()
    {
        var metadata = new Dictionary<string, string> { ["modeldeploymentname"] = "gpt-5.4-mini" };

        Assert.True(MetricMetadata.TryGetDimension(metadata, "ModelDeploymentName", out var value));
        Assert.Equal("gpt-5.4-mini", value);
    }

    [Fact]
    public void TryGetDimension_matchesStatusCode_caseInsensitively()
    {
        var metadata = new Dictionary<string, string> { ["statuscode"] = "200" };

        Assert.True(MetricMetadata.TryGetDimension(metadata, "StatusCode", out var value));
        Assert.Equal("200", value);
    }

    [Fact]
    public void TryGetDimension_missingDimension_returnsFalseAndEmpty()
    {
        var metadata = new Dictionary<string, string> { ["region"] = "eastus2" };

        Assert.False(MetricMetadata.TryGetDimension(metadata, "ModelDeploymentName", out var value));
        Assert.Equal(string.Empty, value);
    }
}
