namespace Sms.Shared.Kernel.Auth;

public sealed record UserRecord(
    Guid Id, Guid? TenantId, string? Email, string? StudentId, string? Phone,
    string? PasswordHash, bool IsPlatform, string Status, string? Name, bool MustSetPassword,
    DateTime CreatedAt, string? PhotoUrl);
