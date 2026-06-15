namespace Sms.Api.Auth;

public sealed record LoginRequest(
    string? Email, string? Password, string? StudentId, string? Phone, string? Role, Guid? TenantId);

public sealed record TokenResponse(string AccessToken, string RefreshToken);
public sealed record RefreshRequest(string RefreshToken);
public sealed record OtpRequest(string Identifier);
public sealed record OtpVerifyRequest(string Identifier, string Code);
public sealed record SetPasswordRequest(string Password);
