using LandDoc.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LandDoc.Tests;

/// <summary>
/// Hosts the API in-process for integration tests and swaps the real <see cref="IChatClient"/> for a
/// deterministic <see cref="FakeChatClient"/>. The real <c>LocalEmbeddingClient</c> is left in place —
/// it's deterministic and offline, so it runs for real.
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
