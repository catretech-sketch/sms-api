using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

/// Email channel: generates the OTP code, enqueues the email for out-of-band delivery, returns the code.
public sealed class EmailOtpSender(IEmailQueue queue) : IOtpSender
{
    public Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var body = $"Your verification code is {code}. It expires in 10 minutes. " +
                   "If you didn't request this, you can ignore this email.";
        queue.Enqueue(new EmailMessage(identifier, "Your verification code", body));
        return Task.FromResult(code);
    }
}
