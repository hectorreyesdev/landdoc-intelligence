namespace LandDoc.Api.Storage;

/// <summary>
/// Reads a dimension value from an Azure Monitor time-series' metadata. Azure returns split-dimension keys
/// <b>lower-cased</b> (e.g. <c>modeldeploymentname</c> for the <c>ModelDeploymentName</c> dimension,
/// <c>statuscode</c> for <c>StatusCode</c>) and the SDK exposes them in a case-sensitive dictionary — so
/// dimension lookups MUST be case-insensitive, or every split series is silently skipped (ADR-0020 / spec
/// 0009; this was the live-metrics bug where token/per-deployment/status values all read as zero).
/// </summary>
internal static class MetricMetadata
{
    public static bool TryGetDimension(IReadOnlyDictionary<string, string> metadata, string dimension, out string value)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        foreach (var entry in metadata)
        {
            if (string.Equals(entry.Key, dimension, StringComparison.OrdinalIgnoreCase))
            {
                value = entry.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
