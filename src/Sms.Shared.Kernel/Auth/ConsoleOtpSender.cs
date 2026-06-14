using System.Security.Cryptography;

namespace Sms.Shared.Kernel.Auth;

public sealed class ConsoleOtpSender : IOtpSender
{
    public Task<string> SendAsync(string phone, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Console.WriteLine($"[OTP] {phone} -> {code}"); // stub; replaced by real SMS provider in Phase 6
        return Task.FromResult(code);
    }
}
