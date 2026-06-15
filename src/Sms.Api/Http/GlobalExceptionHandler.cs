using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Http;

/// Catches any unhandled exception, logs the full detail server-side, and returns the standard
/// snake_case error envelope with a generic message (no internals leaked to the client).
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = new SnakeCaseNamingPolicy() };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(
            ErrorEnvelope.From(new Error("internal_error", "An unexpected error occurred.")), Json);
        await httpContext.Response.WriteAsync(body, ct);
        return true;
    }
}
