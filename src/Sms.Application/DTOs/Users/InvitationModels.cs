namespace Sms.Application.DTOs.Users;

public sealed record InvitationResponse(
    Guid Id,
    string? Email,
    string? Phone,
    string RoleLabel,
    DateTime InvitedAt,
    DateTime ExpiresAt,
    string Status);
