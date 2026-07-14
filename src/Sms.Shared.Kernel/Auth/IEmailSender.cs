namespace Sms.Shared.Kernel.Auth;

/// Transport seam for actually delivering an email (SMTP). Used only by the background worker.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
