using Azure.Identity;
using LandDoc.Api.Extraction;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using LandDoc.Api.Qa;
using LandDoc.Api.Retrieval;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Secrets from Azure Key Vault (prod secret store, ADR-0016). Opt-in: the vault source is added only
// when KeyVault:Uri is set, so tests and offline runs need no cloud credential. Vault secret names use
// the `--` convention (e.g. AzureOpenAI--ApiKey → AzureOpenAI:ApiKey), so they overlay the existing
// config keys with no adapter change. DefaultAzureCredential resolves to your `az login` locally and to
// the container's managed identity in Azure Container Apps — same code, no secrets baked into the image.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// Error model: RFC 7807 ProblemDetails.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Options bound from configuration.
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection("Chunking"));
builder.Services.Configure<RetrievalOptions>(builder.Configuration.GetSection("Retrieval"));
builder.Services.Configure<ModelClientOptions>(builder.Configuration.GetSection("ModelClient"));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection("Anthropic"));
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection("Search"));

// Vector store — config-selected adapter (VectorStore:Provider). Live default: azuresearch (ADR-0017);
// inmemory is the offline/test provider (pinned by TestModuleInitializer). Singleton so ingest (write)
// and retrieval (read) share one instance.
var vectorStoreProvider = builder.Configuration["VectorStore:Provider"] ?? "azuresearch";
builder.Services.AddSingleton<IVectorStore>(sp => vectorStoreProvider.ToLowerInvariant() switch
{
    "inmemory" => new InMemoryVectorStore(),
    "azuresearch" => new AzureAiSearchVectorStore(
        sp.GetRequiredService<IOptions<SearchOptions>>(),
        sp.GetRequiredService<IOptions<EmbeddingOptions>>()),
    _ => throw new InvalidOperationException($"Unknown VectorStore:Provider '{vectorStoreProvider}'."),
});

// Embeddings — config-selected adapter (ModelClient:EmbeddingProvider). Live slice default: azureopenai
// (ADR-0013); local is the deterministic offline fallback. Tests pin local via LandDocApiFactory.
var embeddingProvider = builder.Configuration["ModelClient:EmbeddingProvider"] ?? "azureopenai";
builder.Services.AddSingleton<IEmbeddingClient>(sp => embeddingProvider.ToLowerInvariant() switch
{
    "local" => new LocalEmbeddingClient(sp.GetRequiredService<IOptions<EmbeddingOptions>>()),
    "azureopenai" => new AzureOpenAIEmbeddingClient(
        sp.GetRequiredService<IOptions<AzureOpenAIOptions>>(),
        sp.GetRequiredService<IOptions<EmbeddingOptions>>()),
    _ => throw new InvalidOperationException($"Unknown ModelClient:EmbeddingProvider '{embeddingProvider}'."),
});

// Chat — config-selected adapter (ModelClient:ChatProvider). Live slice default: azureopenai (ADR-0012);
// anthropic is the config-swap fallback. Tests inject a fake IChatClient via ConfigureTestServices.
var chatProvider = builder.Configuration["ModelClient:ChatProvider"] ?? "azureopenai";
builder.Services.AddSingleton<IChatClient>(sp => chatProvider.ToLowerInvariant() switch
{
    "azureopenai" => new AzureOpenAIChatClient(sp.GetRequiredService<IOptions<AzureOpenAIOptions>>()),
    "anthropic" => new AnthropicChatClient(sp.GetRequiredService<IOptions<AnthropicOptions>>()),
    _ => throw new InvalidOperationException($"Unknown ModelClient:ChatProvider '{chatProvider}'."),
});

// Retrieval (read path): question → embed → top-k (spec 0004).
builder.Services.AddScoped<ChunkRetriever>();

// Ingestion pipeline (write path): parse → extract → chunk → embed → store.
builder.Services.AddScoped<PdfTextExtractor>();
builder.Services.AddScoped<TextChunker>();
builder.Services.AddScoped<FieldExtractor>();
builder.Services.AddScoped<DocumentIngestionService>();

var app = builder.Build();

// Unhandled exceptions surface as ProblemDetails.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve the built React SPA from wwwroot (single image, single origin — same-origin, no CORS).
// Registered before the API maps so the static-file/default-document middleware can short-circuit
// asset requests; the API routes below still match first for their exact paths.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDocumentsEndpoints();
app.MapAskEndpoints();

// Any non-API path falls back to the SPA shell so client-side routing works on deep links/refresh.
// /documents and /ask are matched by the endpoint maps above, so this never shadows the API.
app.MapFallbackToFile("index.html");

app.Run();

// Exposed so WebApplicationFactory<Program> can host the app in integration tests.
public partial class Program { }
