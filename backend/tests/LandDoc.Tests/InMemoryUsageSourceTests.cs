using LandDoc.Api.Model;
using LandDoc.Api.Storage;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0009 / ADR-0020 — the offline <see cref="InMemoryUsageSource"/>: canned per-day aggregates scaled by
/// range (wider range = strict superset), real From/To window, and an empty template → all-zero aggregates.
/// </summary>
public sealed class InMemoryUsageSourceTests
{
    [Fact]
    public async Task Default_24h_returnsCannedAggregates()
    {
        var data = await new InMemoryUsageSource().GetUsageAsync(UsageRange.Last24h);

        Assert.Equal(2, data.Deployments.Count);

        var gpt = data.Deployments.Single(d => d.Deployment == "gpt-5.4-mini");
        Assert.Equal(120_000, gpt.PromptTokens);
        Assert.Equal(30_000, gpt.CompletionTokens);
        Assert.Equal(150_000, gpt.TotalTokens);

        var emb = data.Deployments.Single(d => d.Deployment == "text-embedding-3-small");
        Assert.Equal(50_000, emb.PromptTokens);
        Assert.Equal(0, emb.CompletionTokens);

        // Totals are the sum across deployments; TotalTokens == prompt + completion.
        Assert.Equal(170_000, data.Totals.PromptTokens);
        Assert.Equal(30_000, data.Totals.CompletionTokens);
        Assert.Equal(200_000, data.Totals.TotalTokens);
        Assert.Equal(data.Totals.PromptTokens + data.Totals.CompletionTokens, data.Totals.TotalTokens);

        Assert.Equal(400, data.Requests.Total);
        Assert.Equal(380, data.Requests.Success);
        Assert.Equal(10, data.Requests.Throttled429);
        Assert.Equal(850.0, data.Latency.AvgMs);
        Assert.Equal(4200.0, data.Latency.MaxMs);
    }

    [Theory]
    [InlineData(UsageRange.Last24h, 1)]
    [InlineData(UsageRange.Last7d, 7)]
    [InlineData(UsageRange.Last30d, 30)]
    public async Task Window_matchesRange(UsageRange range, int days)
    {
        var data = await new InMemoryUsageSource().GetUsageAsync(range);
        Assert.Equal(days, (data.To - data.From).TotalDays, precision: 6);
    }

    [Fact]
    public async Task WiderRange_scalesTokensAndRequests_andIsAStrictSuperset()
    {
        var source = new InMemoryUsageSource();
        var day = await source.GetUsageAsync(UsageRange.Last24h);
        var week = await source.GetUsageAsync(UsageRange.Last7d);
        var month = await source.GetUsageAsync(UsageRange.Last30d);

        // Token + request counts scale linearly with the window length.
        Assert.Equal(day.Totals.TotalTokens * 7, week.Totals.TotalTokens);
        Assert.Equal(day.Totals.TotalTokens * 30, month.Totals.TotalTokens);
        Assert.Equal(day.Requests.Total * 7, week.Requests.Total);

        // Strict superset: a wider window has strictly more usage than a narrower one.
        Assert.True(week.Totals.TotalTokens > day.Totals.TotalTokens);
        Assert.True(month.Totals.TotalTokens > week.Totals.TotalTokens);

        // Latency is a rate, not a count — unchanged across ranges.
        Assert.Equal(day.Latency.AvgMs, month.Latency.AvgMs);
    }

    [Fact]
    public async Task EmptyTemplate_yieldsAllZeros()
    {
        var empty = new UsageData(
            default,
            default,
            new TokenAggregate(0, 0, 0),
            [],
            new RequestSummary(0, 0, 0, 0, 0),
            new LatencySummary(0, 0));

        var data = await new InMemoryUsageSource(empty).GetUsageAsync(UsageRange.Last30d);

        Assert.Empty(data.Deployments);
        Assert.Equal(0, data.Totals.TotalTokens);
        Assert.Equal(0, data.Requests.Total);
        Assert.Equal(0.0, data.Latency.AvgMs);
        Assert.Equal(0.0, data.Latency.MaxMs);
    }
}
