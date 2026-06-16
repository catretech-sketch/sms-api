using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Sms.Shared.Kernel.Auth;

/// Real SMTP delivery via MailKit. The only untested I/O shim — exercised by the EmailDispatchWorker.
public sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(options.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        var secure = options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(options.Host, options.Port, secure, ct);
        if (!string.IsNullOrEmpty(options.User))
            await client.AuthenticateAsync(options.User, options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
