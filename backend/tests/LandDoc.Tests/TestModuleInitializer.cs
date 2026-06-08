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
    }
}
