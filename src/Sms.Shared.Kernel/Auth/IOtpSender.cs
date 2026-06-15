namespace Sms.Shared.Kernel.Auth;

public interface IOtpSender
{
    /// Sends an OTP to the identifier (email or phone) over the channel ("email"|"sms")
    /// and returns the plaintext code (caller hashes + stores it). Real delivery = Track C.
    Task<string> SendAsync(string identifier, string channel, CancellationToken ct = default);
}
