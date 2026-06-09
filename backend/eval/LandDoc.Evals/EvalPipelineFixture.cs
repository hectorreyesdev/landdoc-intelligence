using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.Search.Documents.Indexes;
using LandDoc.Api.Ingestion;
using LandDoc.Evals.Evaluators;
using LandDoc.Evals.Judge;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LandDoc.Evals;

/// <summary>
/// xUnit fixture (spec 0012, ADR-0021) that boots the REAL pipeline against the FULL production stack —
/// Azure OpenAI chat + embeddings, Azure AI Search — via <see cref="WebApplicationFactory{Program}"/>
/// with <b>no fakes</b> (the opposite of <c>tests/LandDoc.Tests/LandDocApiFactory.cs</c>). It isolates
/// every run to a dedicated Azure AI Search index <c>landdoc-eval-{runId}</c> so the live
/// <c>landdoc-chunks</c> index is never touched, ingests the curated corpus once via <c>POST /documents</c>,
/// and builds the <see cref="ReportingConfiguration"/> (3 evaluators + Sonnet judge + disk result store).
/// <para>
/// Teardown leaves <b>no residue</b> from a local run: each ingested document is removed via
/// <c>DELETE /documents/{id}</c> (which clears both its Blob document and its Search chunks — spec 0008,
/// ADR-0018), then the eval Search index itself is deleted. Both are best-effort with a logged warning —
/// a leaked, uniquely-named <c>landdoc-eval-*</c> index is harmless.
/// </para>
/// Requires real secrets (Search, AzureOpenAI, Anthropic) — see the project README. It cannot run offline.
/// </summary>
public sealed class EvalPipelineFixture : IAsyncLifetime
{
    /// <summary>
    /// The curated, type-DIVERSE subset ingested for every run: a few documents of each instrument kind
    /// (leases, memoranda, mineral/royalty/warranty/quitclaim deeds, surface-use & easement agreements,
    /// title opinions, grazing leases, amendments, division orders, affidavits, probate orders,
    /// assignments, JOAs, farmouts, AMIs, pooling orders, releases, ratifications, AFEs), including the
    /// cross-linked clusters (Henderson/Loving · Bakken/McKenzie · Pecos Valley/Eddy) that drive
    /// multi-document recall@k. File names must match the <c>expectedSources</c> in
    /// <c>Dataset/questions.json</c>.
    /// </summary>
    private static readonly string[] Corpus =
    [
        // Oil & gas leases
        "01-ogl-midland-tx.pdf",
        "04-ogl-eddy-nm.pdf",
        "11-ogl-belmont-oh.pdf",
        // Memoranda of lease
        "15-memo-karnes-tx.pdf",
        "049-memo-mountrail-nd.pdf",
        // Mineral deeds
        "16-mineral-deed-stephens-ok.pdf",
        "053-mineral-deed-kingfisher-ok.pdf",
        // Royalty deeds
        "17-royalty-deed-reagan-tx.pdf",
        "057-royalty-deed-campbell-wy.pdf",
        // Warranty deeds
        "18-warranty-deed-garfield-co.pdf",
        "061-warranty-deed-richland-mt.pdf",
        // Quitclaim deed
        "19-quitclaim-rio-arriba-nm.pdf",
        // Surface use & damage agreements
        "20-surface-use-dunn-nd.pdf",
        "069-surface-use-howard-tx.pdf",
        // Easements / rights-of-way
        "21-easement-dewitt-tx.pdf",
        "073-easement-lasalle-tx.pdf",
        // Title opinion + Loving "Henderson" cluster
        "22-title-opinion-loving-tx.pdf",
        "27-affidavit-heirship-loving-tx.pdf",
        "28-probate-order-loving-tx.pdf",
        "34-release-loving-tx.pdf",
        // Grazing leases
        "23-grazing-lease-carbon-mt.pdf",
        "081-grazing-harrison-wv.pdf",
        // Amendment
        "24-amendment-lea-nm.pdf",
        // Division orders
        "26-division-order-weld-co.pdf",
        "096-division-order-eddy-nm.pdf",
        // Assignment
        "29-assignment-absc-midland-tx.pdf",
        // McKenzie "Bakken Ridge" cluster (JOA + AFE)
        "30-joa-mckenzie-nd.pdf",
        "36-afe-mckenzie-nd.pdf",
        // Farmout (Pecos Valley / Eddy cluster pairs with 04)
        "31-farmout-eddy-nm.pdf",
        // AMI
        "32-ami-karnes-tx.pdf",
        // Pooling order
        "33-pooling-order-kingfisher-ok.pdf",
        // Ratification
        "35-ratification-lea-nm.pdf",
    ];

    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly EvalWebApplicationFactory _factory;
    private readonly List<Guid> _ingestedDocumentIds = [];
    private readonly ConcurrentBag<EvalCaseSummary> _caseResults = [];
    private string _storageRoot = "";
    private string _judgeModel = SonnetJudge.DefaultModel;

    public EvalPipelineFixture()
    {
        // Unique, isolated Search index per run — never the live landdoc-chunks index.
        EvalIndexName = $"landdoc-eval-{Guid.NewGuid():N}";
        _factory = new EvalWebApplicationFactory(EvalIndexName);
    }

    /// <summary>Scenarios record each case's scores here; the fixture writes the snapshot on dispose.</summary>
    public void Record(EvalCaseSummary result) => _caseResults.Add(result);

    /// <summary>The per-run isolated Azure AI Search index name.</summary>
    public string EvalIndexName { get; }

    /// <summary>HTTP client bound to the in-process API (full prod stack).</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>Resolved app configuration (judge key, Search creds, threshold flags).</summary>
    public IConfiguration Configuration { get; private set; } = null!;

    /// <summary>Reporting config (3 evaluators + Sonnet judge + disk result store) shared across cases.</summary>
    public ReportingConfiguration Reporting { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Client = _factory.CreateClient();
        Configuration = _factory.Services.GetRequiredService<IConfiguration>();

        // Judge (Sonnet 4.6) + the disk result store the `aieval` HTML report reads from.
        var chatConfiguration = SonnetJudge.CreateChatConfiguration(Configuration);
        _judgeModel = Configuration["Eval:JudgeModel"] is { Length: > 0 } configured ? configured : SonnetJudge.DefaultModel;
        _storageRoot = Path.Combine(AppContext.BaseDirectory, "eval-results");
        Directory.CreateDirectory(_storageRoot);

        Reporting = DiskBasedReportingConfiguration.Create(
            _storageRoot,
            [new RecallAtKEvaluator(), new GroundednessEvaluator(), new EquivalenceEvaluator()],
            chatConfiguration);

        // Ingest the curated corpus once into the isolated index, capturing the document ids for teardown.
        foreach (var fileName in Corpus)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "samples", fileName);
            var bytes = await File.ReadAllBytesAsync(path);

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", fileName);

            var response = await Client.PostAsync("/documents", form);
            response.EnsureSuccessStatusCode();

            var ingested = await response.Content.ReadFromJsonAsync<IngestDocumentResponse>();
            if (ingested is not null)
            {
                _ingestedDocumentIds.Add(ingested.Id);
            }
        }
    }

    public async Task DisposeAsync()
    {
        // 1) Remove each ingested document — clears its Blob document AND its Search chunks (spec 0008).
        foreach (var id in _ingestedDocumentIds)
        {
            try
            {
                var response = await Client.DeleteAsync($"/documents/{id}");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[eval-teardown] WARN: failed to delete document {id}: {ex.Message}");
            }
        }

        // 2) Delete the isolated eval index itself (the now-empty index object). Best-effort.
        try
        {
            var endpoint = Configuration["Search:Endpoint"];
            var apiKey = Configuration["Search:ApiKey"];
            if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
            {
                var indexClient = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                await indexClient.DeleteIndexAsync(EvalIndexName);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[eval-teardown] WARN: failed to delete eval index '{EvalIndexName}': {ex.Message}. " +
                "A leaked, uniquely-named landdoc-eval-* index is harmless.");
        }

        WriteSummary();

        Client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Writes the SPA-friendly <c>eval-summary.json</c> (spec 0011) to the result-store root from the
    /// scores the scenarios recorded. Promote this file into the frontend + commit it to refresh the
    /// Dashboard scorecard. Best-effort — a write failure must not fail the run.
    /// </summary>
    private void WriteSummary()
    {
        if (_caseResults.IsEmpty)
        {
            return;
        }

        try
        {
            var cases = _caseResults.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

            double? Mean(Func<EvalCaseSummary, double?> select)
            {
                var values = cases.Select(select).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return values.Count == 0 ? null : Math.Round(values.Average(), 2);
            }

            var summary = new EvalSummary(
                DateTimeOffset.UtcNow.ToString("o"),
                _judgeModel,
                cases.Count,
                new EvalMeans(Mean(c => c.RecallAtK), Mean(c => c.Groundedness), Mean(c => c.Equivalence)),
                cases);

            File.WriteAllText(
                Path.Combine(_storageRoot, "eval-summary.json"),
                JsonSerializer.Serialize(summary, SummaryJsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[eval-summary] WARN: failed to write eval-summary.json: {ex.Message}");
        }
    }
}

/// <summary>
/// <see cref="WebApplicationFactory{Program}"/> wired for the full production stack. Unlike the green
/// suite's factory it injects <b>no fakes</b>: it layers <c>appsettings.eval.json</c> (provider selection)
/// and the per-run unique <c>Search:IndexName</c> on top of the app's config; the real Azure OpenAI / Azure
/// AI Search / Azure Blob adapters are resolved from secrets (env / user-secrets / Key Vault).
/// </summary>
internal sealed class EvalWebApplicationFactory(string evalIndexName) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.eval.json"),
                optional: false,
                reloadOnChange: false);

            // Highest precedence: the unique, isolated eval index for this run.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Search:IndexName"] = evalIndexName,
            });
        });
    }
}
