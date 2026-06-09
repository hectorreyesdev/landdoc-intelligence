using LandDoc.Api.Model;

namespace LandDoc.Api.Usage;

/// <summary>
/// Maps the <c>range</c> query value (<c>24h</c> / <c>7d</c> / <c>30d</c>) to/from <see cref="UsageRange"/>
/// (spec 0009). An omitted/blank value defaults to 24h; any other value is rejected (→ 400 at the endpoint).
/// </summary>
public static class UsageRanges
{
    public static bool TryParse(string? value, out UsageRange range)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "24h":
                range = UsageRange.Last24h;
                return true;
            case "7d":
                range = UsageRange.Last7d;
                return true;
            case "30d":
                range = UsageRange.Last30d;
                return true;
            default:
                range = UsageRange.Last24h;
                return false;
        }
    }

    /// <summary>The canonical wire form echoed back in the response.</summary>
    public static string ToWire(UsageRange range) => range switch
    {
        UsageRange.Last24h => "24h",
        UsageRange.Last7d => "7d",
        UsageRange.Last30d => "30d",
        _ => "24h",
    };
}
