using System.Net;
using System.Net.Http.Json;
using LandDoc.Api.Model;
using LandDoc.Api.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0009 / ADR-0020 — the <c>GET /usage</c> endpoint, hosted with the in-memory usage source
/// (<c>UsageSource:Provider=inmemory</c>, pinned assembly-wide). Verifies the documented response shape,
/// range parsing (default 24h, scaling, invalid → 400), and that a no-data window returns zeros with 200.
/// </summary>
public sealed class UsageEndpointTests
{
    private sealed record UsageReportDto(
        string Range,
        DateTimeOffset From,
        DateTimeOffset To,
        TotalsDto Totals,
        List<DeploymentDto> Deployments,
        RequestsDto Requests,
        LatencyDto Latency);

    private sealed record TotalsDto(long PromptTokens, long CompletionTokens, long TotalTokens, decimal EstimatedCostUsd);

    private sealed record DeploymentDto(string Deployment, long PromptTokens, long CompletionTokens, long TotalTokens, decimal EstimatedCostUsd);

    private sealed record RequestsDto(long Total, long Success, long ClientErrors, long Throttled429, long ServerErrors);

    private sealed record LatencyDto(double AvgMs, double MaxMs);

    [Fact]
    public async Task GET_usage_default_returns200_withDocumentedShape()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/usage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<UsageReportDto>();
        Assert.NotNull(report);

        // Range defaults to 24h when omitted, and the window is echoed.
        Assert.Equal("24h", report!.Range);
        Assert.True(report.To > report.From);

        // Totals: prompt + completion == total (canned per-day numbers).
        Assert.Equal(170_000, report.Totals.PromptTokens);
        Assert.Equal(30_000, report.Totals.CompletionTokens);
        Assert.Equal(200_000, report.Totals.TotalTokens);

        // Per-deployment rows present.
        Assert.Equal(2, report.Deployments.Count);
        Assert.Contains(report.Deployments, d => d.Deployment == "gpt-5.4-mini");
        Assert.Contains(report.Deployments, d => d.Deployment == "text-embedding-3-small");

        // Request + latency summaries.
        Assert.Equal(400, report.Requests.Total);
        Assert.Equal(10, report.Requests.Throttled429);
        Assert.Equal(850.0, report.Latency.AvgMs);
        Assert.Equal(4200.0, report.Latency.MaxMs);

        // Cost is computed (> 0) and totals == sum of per-deployment cost.
        Assert.True(report.Totals.EstimatedCostUsd > 0m);
        Assert.Equal(report.Deployments.Sum(d => d.EstimatedCostUsd), report.Totals.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    public async Task GET_usage_range_scalesAndEchoes(string range, int multiplier)
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/usage?range={range}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<UsageReportDto>();
        Assert.NotNull(report);
        Assert.Equal(range, report!.Range);
        Assert.Equal(200_000 * multiplier, report.Totals.TotalTokens);
    }

    [Fact]
    public async Task GET_usage_invalidRange_returns400()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/usage?range=bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GET_usage_noData_returnsZeros_not500()
    {
        var empty = new UsageData(
            default, default, new TokenAggregate(0, 0, 0), [],
            new RequestSummary(0, 0, 0, 0, 0), new LatencySummary(0, 0));

        using var factory = new LandDocApiFactory().WithWebHostBuilder(b =>
            b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUsageSource>();
                services.AddSingleton<IUsageSource>(new InMemoryUsageSource(empty));
            }));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/usage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<UsageReportDto>();
        Assert.NotNull(report);
        Assert.Empty(report!.Deployments);
        Assert.Equal(0, report.Totals.TotalTokens);
        Assert.Equal(0m, report.Totals.EstimatedCostUsd);
        Assert.Equal(0, report.Requests.Total);
    }
}
