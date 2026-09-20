namespace Sms.Shared.Kernel.Auth;

/// Hand-off between the request path (Enqueue) and the background worker (DequeueAsync).
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
    ValueTask<EmailMessage> DequeueAsync(CancellationToken ct);
}
