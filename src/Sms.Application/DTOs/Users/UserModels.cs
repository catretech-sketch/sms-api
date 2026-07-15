namespace Sms.Application.DTOs.Users;

public sealed record InviteUserRequest(string? Email, string? Phone, string[] Roles);
public sealed record ImportUsersRequest(ImportRowDto[] Rows);
public sealed record ImportRowDto(string? Email, string? Phone, string? Role);
public sealed record ImportError(int Row, string Reason);
public sealed record ImportResponse(int Created, int Skipped, IReadOnlyList<ImportError> Errors);

/** Tenant user row keyed by user id — roles & permissions attached by id. */
public sealed record SchoolUserResponse(
    Guid Id,
    string? Email,
    string? Phone,
    string Status,
    DateTime CreatedAt,
    string[] Roles);

public sealed record PermissionOverrideDto(string Module, string Cap, string Effect);
public sealed record SetUserPermissionsRequest(PermissionOverrideDto[] Overrides);
public sealed record SetUserRolesRequest(string[] Roles);
