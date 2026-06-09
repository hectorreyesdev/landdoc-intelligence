namespace LandDoc.Api.Model;

/// <summary>
/// The time window for a usage query (spec 0009). Maps to an Azure Monitor query timespan in the live
/// adapter; the wire form echoed in the response is <c>24h</c> / <c>7d</c> / <c>30d</c>.
/// </summary>
public enum UsageRange
{
    Last24h,
    Last7d,
    Last30d,
}
