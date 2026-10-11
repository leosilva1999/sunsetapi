using Serilog.Context;

namespace Sunset.API.Middlewares;

// Surfaces ASP.NET's per-request TraceIdentifier as the request's public id: it goes out in the
// X-Request-Id response header (and in the body of every error handled by this API), and it's
// attached to every log line written while the request runs, so a client-reported id can be
// grepped straight to the matching log lines. An id sent by the client is deliberately ignored -
// trusting it would let a caller forge or collide with ids in the logs.
public class RequestIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("RequestId", requestId))
            await next(context);
    }
}
