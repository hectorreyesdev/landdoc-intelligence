using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using LandDoc.Api.Model;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Storage;

/// <summary>
/// Live usage source (ADR-0020): reads Azure Monitor PLATFORM metrics for the Foundry resource via
/// <see cref="MetricsQueryClient"/> + <see cref="DefaultAzureCredential"/> (managed identity in hosting,
/// <c>az login</c> locally — consistent with ADR-0016). Read-only (Monitoring Reader); no secret. Token and
/// request metrics are summed and split by the <c>ModelDeploymentName</c> dimension for the per-deployment
/// rows; requests additionally carry the <c>StatusCode</c> dimension for the health buckets; latency is
/// averaged with a maximum. Selected via <c>UsageSource:Provider=azuremonitor</c>; the in-memory source is
/// the offline/test default. Throws on construction if <c>Monitor:ResourceId</c> is unset (fail fast).
/// </summary>
public sealed class AzureMonitorUsageSource : IUsageSource
{
    private const string PromptTokensMetric = "ProcessedPromptTokens";
    private const string GeneratedTokensMetric = "GeneratedTokens";
    private const string RequestsMetric = "AzureOpenAIRequests";
    // AIServices (Foundry) resources expose "Latency" (ms), not the classic "AzureOpenAITimeToResponse".
    private const string LatencyMetric = "Latency";
    private const string DeploymentDimension = "ModelDeploymentName";
    private const string StatusCodeDimension = "StatusCode";

    private readonly MetricsQueryClient _client;
    private readonly string _resourceId;

    public AzureMonitorUsageSource(IOptions<MonitorOptions> options)
    {
        var resourceId = options.Value.ResourceId;
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException(
                "Monitor:ResourceId must be set when UsageSource:Provider is 'azuremonitor'.");
        }

        _resourceId = resourceId;
        _client = new MetricsQueryClient(new DefaultAzureCredential());
    }

    public async Task<UsageData> GetUsageAsync(UsageRange range, CancellationToken ct = default)
    {
        var window = WindowFor(range);
        var to = DateTimeOffset.UtcNow;
        var from = to - window;

        var tokens = await _client.QueryResourceAsync(
            _resourceId,
            [PromptTokensMetric, GeneratedTokensMetric],
            new MetricsQueryOptions
            {
                TimeRange = new QueryTimeRange(window),
                Aggregations = { MetricAggregationType.Total },
                Filter = $"{DeploymentDimension} eq '*'",
            },
            ct);

        var requests = await _client.QueryResourceAsync(
            _resourceId,
            [RequestsMetric],
            new MetricsQueryOptions
            {
                TimeRange = new QueryTimeRange(window),
                Aggregations = { MetricAggregationType.Total },
                Filter = $"{StatusCodeDimension} eq '*'",
            },
            ct);

        var latency = await _client.QueryResourceAsync(
            _resourceId,
            [LatencyMetric],
            new MetricsQueryOptions
            {
                TimeRange = new QueryTimeRange(window),
                Aggregations = { MetricAggregationType.Average, MetricAggregationType.Maximum },
            },
            ct);

        var deployments = BuildDeployments(tokens.Value);
        var totals = new TokenAggregate(
            deployments.Sum(d => d.PromptTokens),
            deployments.Sum(d => d.CompletionTokens),
            deployments.Sum(d => d.TotalTokens));

        return new UsageData(from, to, totals, deployments, BuildRequests(requests.Value), BuildLatency(latency.Value));
    }

    private static IReadOnlyList<DeploymentUsage> BuildDeployments(MetricsQueryResult result)
    {
        var prompt = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var completion = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var metric in result.Metrics)
        {
            var bucket = metric.Name == GeneratedTokensMetric ? completion : prompt;
            foreach (var series in metric.TimeSeries)
            {
                if (!MetricMetadata.TryGetDimension(series.Metadata, DeploymentDimension, out var deployment))
                {
                    continue;
                }

                var sum = (long)series.Values.Sum(v => v.Total ?? 0d);
                bucket[deployment] = bucket.GetValueOrDefault(deployment) + sum;
            }
        }

        return prompt.Keys
            .Union(completion.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var p = prompt.GetValueOrDefault(name);
                var c = completion.GetValueOrDefault(name);
                return new DeploymentUsage(name, p, c, p + c);
            })
            .OrderBy(d => d.Deployment, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RequestSummary BuildRequests(MetricsQueryResult result)
    {
        long total = 0, success = 0, clientErrors = 0, throttled = 0, serverErrors = 0;

        foreach (var metric in result.Metrics)
        {
            foreach (var series in metric.TimeSeries)
            {
                var count = (long)series.Values.Sum(v => v.Total ?? 0d);
                total += count;

                if (!MetricMetadata.TryGetDimension(series.Metadata, StatusCodeDimension, out var statusCode) ||
                    !int.TryParse(statusCode, out var status))
                {
                    continue;
                }

                if (status == 429)
                {
                    throttled += count;
                }
                else if (status >= 500)
                {
                    serverErrors += count;
                }
                else if (status >= 400)
                {
                    clientErrors += count;
                }
                else if (status is >= 200 and < 300)
                {
                    success += count;
                }
            }
        }

        return new RequestSummary(total, success, clientErrors, throttled, serverErrors);
    }

    private static LatencySummary BuildLatency(MetricsQueryResult result)
    {
        double avg = 0d;
        double max = 0d;

        foreach (var metric in result.Metrics)
        {
            foreach (var series in metric.TimeSeries)
            {
                var averages = series.Values.Where(v => v.Average.HasValue).Select(v => v.Average!.Value).ToList();
                if (averages.Count > 0)
                {
                    avg = averages.Average();
                }

                foreach (var value in series.Values.Where(v => v.Maximum.HasValue))
                {
                    max = Math.Max(max, value.Maximum!.Value);
                }
            }
        }

        return new LatencySummary(avg, max);
    }

    private static TimeSpan WindowFor(UsageRange range) => range switch
    {
        UsageRange.Last24h => TimeSpan.FromHours(24),
        UsageRange.Last7d => TimeSpan.FromDays(7),
        UsageRange.Last30d => TimeSpan.FromDays(30),
        _ => TimeSpan.FromHours(24),
    };
}
