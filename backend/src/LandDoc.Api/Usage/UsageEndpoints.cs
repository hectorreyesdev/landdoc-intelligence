using LandDoc.Api.Storage;

namespace LandDoc.Api.Usage;

/// <summary>
/// Maps the ops/usage read surface (spec 0009): <c>GET /usage?range=24h|7d|30d</c> reads raw aggregates from
/// the config-selected <see cref="IUsageSource"/>, computes estimated cost via <see cref="UsageCostCalculator"/>,
/// and returns the <c>UsageReport</c>. Range defaults to 24h; an unrecognized range is a 400 ProblemDetails; a
/// no-data window returns zeros with 200 (never 500). Read-only — never mutates anything.
/// </summary>
public static class UsageEndpoints
{
    public static IEndpointRouteBuilder MapUsageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/usage", async (
            string? range,
            IUsageSource usage,
            UsageCostCalculator cost,
            CancellationToken ct) =>
        {
            if (!UsageRanges.TryParse(range, out var parsed))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The 'range' query parameter must be one of '24h', '7d', or '30d'.");
            }

            var data = await usage.GetUsageAsync(parsed, ct);
            return Results.Ok(cost.ToReport(data, parsed));
        });

        return app;
    }
}
