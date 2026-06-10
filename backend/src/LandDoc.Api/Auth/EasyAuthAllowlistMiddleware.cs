using Microsoft.Extensions.Options;

namespace LandDoc.Api.Auth;

/// <summary>
/// Defense-in-depth allowlist behind the Container Apps Easy Auth platform gate (spec 0013, ADR-0022).
/// When <c>Auth:Mode=easyauth</c>, every request — API routes and SPA static files alike — must carry
/// the <c>X-MS-CLIENT-PRINCIPAL-ID</c> header the platform injects after sign-in: missing → 401,
/// present but not allowlisted → 403. Authentication itself is Easy Auth's job — this middleware never
/// parses tokens or the claims blob, it only checks <em>which</em> authenticated principal arrived.
/// With <c>Auth:Mode=none</c> (the default) it passes every request through untouched.
/// </summary>
public sealed class EasyAuthAllowlistMiddleware
{
    private const string PrincipalIdHeader = "X-MS-CLIENT-PRINCIPAL-ID";

    private readonly RequestDelegate _next;
    private readonly bool _enforce;
    private readonly HashSet<string> _allowedPrincipalIds;

    public EasyAuthAllowlistMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
    {
        _next = next;
        _enforce = string.Equals(options.Value.Mode, AuthOptions.ModeEasyAuth, StringComparison.OrdinalIgnoreCase);
        _allowedPrincipalIds = new HashSet<string>(options.Value.AllowedPrincipalIds, StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enforce)
        {
            await _next(context);
            return;
        }

        var principalId = context.Request.Headers[PrincipalIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(principalId))
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication required.")
                .ExecuteAsync(context);
            return;
        }

        if (!_allowedPrincipalIds.Contains(principalId))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This account is not allowed to access this app.")
                .ExecuteAsync(context);
            return;
        }

        await _next(context);
    }
}
