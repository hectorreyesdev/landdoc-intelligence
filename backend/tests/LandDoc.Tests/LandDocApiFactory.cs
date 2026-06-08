using LandDoc.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Hosts the API in-process for integration tests. The assembly-wide <see cref="TestModuleInitializer"/>
/// sets <c>ModelClient__EmbeddingProvider=local</c> so every factory uses the offline
/// <see cref="LocalEmbeddingClient"/> regardless of the appsettings default. Swaps
/// <see cref="IChatClient"/> for a deterministic <see cref="FakeChatClient"/>.
/// </summary>
public sealed class LandDocApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient, FakeChatClient>();
        });
    }
}
