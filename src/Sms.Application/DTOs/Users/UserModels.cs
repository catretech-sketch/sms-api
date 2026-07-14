namespace Sms.Application.DTOs.Users;

public sealed record InviteUserRequest(string? Email, string? Phone, string[] Roles);
public sealed record ImportUsersRequest(ImportRowDto[] Rows);
public sealed record ImportRowDto(string? Email, string? Phone, string? Role);
public sealed record ImportError(int Row, string Reason);
public sealed record ImportResponse(int Created, int Skipped, IReadOnlyList<ImportError> Errors);
