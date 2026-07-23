using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Sms.Shared.Kernel.Auth;

/// Real SMTP delivery via MailKit. The only untested I/O shim — exercised by the EmailDispatchWorker.
public sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder { TextBody = message.Body };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            builder.HtmlBody = message.HtmlBody;
        if (message.AttachmentBytes is { Length: > 0 } bytes
            && !string.IsNullOrWhiteSpace(message.AttachmentFileName))
        {
            builder.Attachments.Add(
                message.AttachmentFileName,
                bytes,
                ContentType.Parse(message.AttachmentContentType ?? "application/pdf"));
        }
        if (message.ExtraAttachments is { Count: > 0 })
        {
            foreach (var a in message.ExtraAttachments)
            {
                if (a.Bytes is not { Length: > 0 }) continue;
                if (string.IsNullOrWhiteSpace(a.FileName)) continue;
                builder.Attachments.Add(
                    a.FileName,
                    a.Bytes,
                    ContentType.Parse(string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType));
            }
        }
        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var secure = options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(options.Host, options.Port, secure, ct);
        if (!string.IsNullOrEmpty(options.User))
            await client.AuthenticateAsync(options.User, options.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
