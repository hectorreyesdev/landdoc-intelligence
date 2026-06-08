using LandDoc.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Hosts the API in-process for integration tests. Pins <c>ModelClient:EmbeddingProvider=local</c> so
/// the offline <see cref="LocalEmbeddingClient"/> is used regardless of the appsettings default (which is
/// now <c>azureopenai</c>), keeping the suite deterministic and credential-free. Swaps
/// <see cref="IChatClient"/> for a deterministic <see cref="FakeChatClient"/>.
/// </summary>
public sealed class LandDocApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ModelClient:EmbeddingProvider", "local");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient, FakeChatClient>();
        });
    }
}
