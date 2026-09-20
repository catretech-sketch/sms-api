namespace Sms.Shared.Kernel.Auth;

/// Routes by channel: "email" -> EmailOtpSender, otherwise ("sms") -> ConsoleOtpSender (stub).
public sealed class ChannelOtpSender(EmailOtpSender email, ConsoleOtpSender sms) : IOtpSender
{
    public Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default) =>
        channel == "email"
            ? email.SendAsync(identifier, channel, ct)
            : sms.SendAsync(identifier, channel, ct);
}
