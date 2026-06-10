using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LandDoc.Tests;

/// <summary>
/// Spec 0013 — the single-user gate: an app-level allowlist on the principal header Easy Auth injects
/// (<c>X-MS-CLIENT-PRINCIPAL-ID</c>). <c>Auth:Mode=none</c> (the suite default) leaves behavior
/// unchanged; <c>easyauth</c> gates API routes and the SPA shell alike (missing header → 401,
/// non-allowlisted → 403, allowlisted → through); <c>easyauth</c> with an empty allowlist fails startup.
/// </summary>
public sealed class AuthMiddlewareTests
{
    private const string PrincipalIdHeader = "X-MS-CLIENT-PRINCIPAL-ID";
    private const string OwnerId = "00000000-0000-0000-0000-000000000001";
    private const string StrangerId = "00000000-0000-0000-0000-000000000002";

    private static WebApplicationFactory<Program> EasyAuthFactory(LandDocApiFactory factory) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Auth:Mode", "easyauth");
            builder.UseSetting("Auth:AllowedPrincipalIds:0", OwnerId);
        });

    [Fact]
    public async Task DefaultMode_none_requestWithoutAuthHeaders_succeeds()
    {
        using var factory = new LandDocApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EasyAuth_missingPrincipalHeader_returns401_onApiRoute()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = EasyAuthFactory(baseFactory);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EasyAuth_missingPrincipalHeader_returns401_onSpaShell()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = EasyAuthFactory(baseFactory);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EasyAuth_nonAllowlistedPrincipal_returns403()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = EasyAuthFactory(baseFactory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PrincipalIdHeader, StrangerId);

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EasyAuth_allowlistedPrincipal_reachesTheEndpoint()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = EasyAuthFactory(baseFactory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PrincipalIdHeader, OwnerId);

        var response = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void EasyAuth_withEmptyAllowlist_failsAtStartup()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:Mode", "easyauth"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AllowedPrincipalIds", exception.Message);
    }

    [Fact]
    public void UnknownAuthMode_failsAtStartup()
    {
        using var baseFactory = new LandDocApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:Mode", "saml"));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Auth:Mode", exception.Message);
    }
}
