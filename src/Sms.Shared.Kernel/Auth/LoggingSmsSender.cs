using Microsoft.Extensions.Logging;

namespace Sms.Shared.Kernel.Auth;

/// Development / stub SMS — logs instead of calling a real SMS gateway.
public interface ISmsSender
{
    Task SendAsync(string toPhone, string body, CancellationToken ct = default);
}

/// Stub — no real SMS gateway is wired up yet, so this is the only delivery path in every
/// environment today. Always "sends" (completes normally) so existing flows keep working, but
/// only logs the body (which can contain OTPs, invite links, or other PII) when isDevelopment —
/// logging it outside Development would leak secrets into production logs.
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger, bool isDevelopment) : ISmsSender
{
    public Task SendAsync(string toPhone, string body, CancellationToken ct = default)
    {
        if (isDevelopment)
            logger.LogWarning("[DEV SMS] To={Phone} Body={Body}", toPhone, body);
        return Task.CompletedTask;
    }
}
