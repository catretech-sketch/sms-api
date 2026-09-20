using Microsoft.Extensions.Logging;

namespace Sms.Shared.Kernel.Auth;

/// Development / stub SMS — logs instead of calling a real SMS gateway.
public interface ISmsSender
{
    Task SendAsync(string toPhone, string body, CancellationToken ct = default);
}

public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string toPhone, string body, CancellationToken ct = default)
    {
        logger.LogWarning("[DEV SMS] To={Phone} Body={Body}", toPhone, body);
        return Task.CompletedTask;
    }
}
