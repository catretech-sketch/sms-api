namespace Sms.Shared.Kernel.Auth;

public sealed class SmtpOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    public string From { get; init; } = "";
    public bool UseStartTls { get; init; } = true;
}
