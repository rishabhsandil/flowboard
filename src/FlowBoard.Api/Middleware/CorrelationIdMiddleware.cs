using Serilog.Context;

namespace FlowBoard.Api.Middleware;

/// <summary>
/// Reads or generates an `X-Correlation-Id` header, stashes it on
/// <see cref="HttpContext.Items"/> for downstream middleware (e.g. the global
/// exception handler) and pushes it onto Serilog's <see cref="LogContext"/>
/// so every log line within the request scope carries the same id.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey    = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers.TryGetValue(HeaderName, out var existing) &&
                 !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = id;
        context.Response.OnStarting(() =>
        {
            // OnStarting fires before headers are flushed, so this is safe.
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await _next(context);
        }
    }
}
