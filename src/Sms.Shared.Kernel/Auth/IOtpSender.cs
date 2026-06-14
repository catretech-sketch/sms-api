namespace Sms.Shared.Kernel.Auth;

public interface IOtpSender
{
    /// Sends an OTP to the phone and returns the plaintext code (caller hashes + stores it).
    Task<string> SendAsync(string phone, CancellationToken ct = default);
}
