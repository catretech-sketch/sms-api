namespace Sms.Shared.Kernel.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "sms";
    public string Audience { get; init; } = "sms-apps";
    public string SigningKey { get; init; } = "";
    public int AccessTokenMinutes { get; init; } = 15;
}
