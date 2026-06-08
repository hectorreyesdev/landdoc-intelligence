using System.Runtime.CompilerServices;

namespace LandDoc.Tests;

internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Force every WebApplicationFactory<Program> in this assembly to use the offline
        // LocalEmbeddingClient regardless of the appsettings default (azureopenai).
        // Double-underscore separates config sections in environment variable names.
        Environment.SetEnvironmentVariable("ModelClient__EmbeddingProvider", "local");

        // Pin the in-memory vector store so CI (no Azure AI Search creds) stays green (ADR-0017).
        Environment.SetEnvironmentVariable("VectorStore__Provider", "inmemory");

        // Pin the in-memory document store so CI (no Azure Storage creds) stays green (ADR-0018).
        Environment.SetEnvironmentVariable("DocumentStore__Provider", "inmemory");
    }
}
