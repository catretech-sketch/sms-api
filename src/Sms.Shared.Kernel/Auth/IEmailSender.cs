namespace Sms.Shared.Kernel.Auth;

/// Transport seam for actually delivering an email (SMTP). Used only by the background worker.
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
