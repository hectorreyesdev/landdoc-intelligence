using LandDoc.Api.Extraction;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using LandDoc.Api.Qa;
using LandDoc.Api.Retrieval;
using LandDoc.Api.Storage;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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

// Storage seam — a singleton so the ingest (write) and retrieval (read) paths share one in-memory store.
builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();

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

app.MapDocumentsEndpoints();
app.MapAskEndpoints();

app.Run();

// Exposed so WebApplicationFactory<Program> can host the app in integration tests.
public partial class Program { }
