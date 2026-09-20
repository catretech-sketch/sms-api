using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

public sealed class ConsoleOtpSender : IOtpSender
{
    public Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Console.WriteLine($"[OTP/{channel}] {identifier} -> {code}"); // stub; real SMS/email = Track C
        return Task.FromResult(code);
    }
}
