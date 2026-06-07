using LandDoc.Api.Extraction;
using LandDoc.Api.Ingestion;
using LandDoc.Api.Model;
using LandDoc.Api.Qa;
using LandDoc.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// Error model: RFC 7807 ProblemDetails.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Options bound from configuration.
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.Configure<ChunkingOptions>(builder.Configuration.GetSection("Chunking"));

// Storage seam — a singleton so the ingest (write) and retrieval (read) paths share one in-memory store.
builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();

// Embeddings — the deterministic local embedder is the slice default (ADR-0008).
builder.Services.AddSingleton<IEmbeddingClient, LocalEmbeddingClient>();

// Chat — config-selected adapter (ModelClient:ChatProvider). Adapters are slice stubs; tests inject a fake.
var chatProvider = builder.Configuration["ModelClient:ChatProvider"] ?? "foundry";
builder.Services.AddSingleton<IChatClient>(_ => chatProvider.ToLowerInvariant() switch
{
    "anthropic" => new AnthropicChatClient(),
    "foundry" => new FoundryChatClient(),
    _ => throw new InvalidOperationException($"Unknown ModelClient:ChatProvider '{chatProvider}'."),
});

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
