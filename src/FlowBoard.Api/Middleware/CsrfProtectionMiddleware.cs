namespace FlowBoard.Api.Middleware;

/// <summary>
/// Defense-in-depth CSRF protection for state-mutating requests.
///
/// This API uses JWT Bearer tokens (stored in localStorage, sent via the
/// Authorization header), which are already CSRF-safe at the protocol level —
/// a cross-site attacker cannot inject the Authorization header. The CORS
/// policy provides browser enforcement. This middleware adds a second layer:
///
///   1. For POST/PUT/PATCH/DELETE requests it requires the
///      <c>X-Requested-With: XMLHttpRequest</c> header. Browsers cannot set
///      custom headers cross-site without a CORS preflight; if our CORS policy
///      rejects the preflight the real request never arrives. Any client that
///      does reach this middleware must therefore have been allowed through CORS.
///
///   2. Auth endpoints (/api/auth/*) are exempt — they are public by design
///      and protected separately by rate limiting.
///
///   3. Swagger UI and same-origin browser requests that omit the header are
///      whitelisted via the <see cref="ExemptPaths"/> list.
/// </summary>
public sealed class CsrfProtectionMiddleware
{
    private static readonly HashSet<string> MutatingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    // Public / tooling endpoints that do not need the header.
    private static readonly string[] ExemptPaths =
    [
        "/api/auth/",
        "/api/health",
        "/swagger",
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<CsrfProtectionMiddleware> _log;

    public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> log)
    {
        _next = next;
        _log  = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresCheck(context) && !HasXmlHttpRequestHeader(context))
        {
            _log.LogWarning(
                "CSRF check failed: missing X-Requested-With on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode  = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "csrf_validation_failed" });
            return;
        }

        await _next(context);
    }

    private static bool RequiresCheck(HttpContext ctx)
    {
        if (!MutatingMethods.Contains(ctx.Request.Method)) return false;

        var path = ctx.Request.Path.Value ?? "";
        foreach (var exempt in ExemptPaths)
            if (path.StartsWith(exempt, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    private static bool HasXmlHttpRequestHeader(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Requested-With", out var val) &&
        val.ToString().Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
}
