namespace FlowBoard.Api.Middleware;

/// <summary>
/// Adds common security response headers to every response.
/// These are defense-in-depth controls that complement the CORS policy
/// and Bearer-token CSRF protection.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"]    = "nosniff";
            h["X-Frame-Options"]           = "DENY";
            h["Referrer-Policy"]           = "strict-origin-when-cross-origin";
            h["X-XSS-Protection"]          = "0"; // rely on CSP, not the legacy filter
            h["Permissions-Policy"]        = "camera=(), microphone=(), geolocation=()";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
