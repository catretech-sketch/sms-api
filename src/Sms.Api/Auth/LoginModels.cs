namespace Sms.Api.Auth;

public sealed record LoginRequest(
    string? Email, string? Password, string? StudentId, string? Phone, string? Role, Guid? TenantId);

public sealed record TokenResponse(string AccessToken, string RefreshToken);
public sealed record RefreshRequest(string RefreshToken);
