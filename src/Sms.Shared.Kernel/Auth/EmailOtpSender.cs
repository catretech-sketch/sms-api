using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Sms.Shared.Kernel.Auth;

/// Email channel: generates the OTP code, enqueues the email for out-of-band delivery, returns the code.
/// When logCode is true (development only), also writes the code to the log so local sign-in works
/// without working SMTP. Defaults keep production behaviour (no logging) and the existing test callers compiling.
public sealed class EmailOtpSender(IEmailQueue queue, ILogger<EmailOtpSender>? log = null, bool logCode = false) : IOtpSender
{
    public Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var body = $"Your verification code is {code}. It expires in 10 minutes. " +
                   "If you didn't request this, you can ignore this email.";
        queue.Enqueue(new EmailMessage(identifier, "Your verification code", body));
        if (logCode)
            log?.LogWarning("[DEV OTP/email] {Identifier} -> {Code} (development only)", identifier, code);
        return Task.FromResult(code);
    }
}
