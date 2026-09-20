namespace Sms.Application.DTOs.Users;

/// <param name="SendWelcome">False suppresses the welcome email/SMS for this call — used when
/// inviting the same person to several schools in one batch, so only one message goes out.</param>
/// <param name="Method">"code" (default) sends a 6-digit OTP; "link" sends a one-click
/// magic login link instead (no code shown in the message).</param>
/// <param name="Channel">"email" or "phone" — which identifier receives the welcome message
/// when both are provided. Defaults to email if set, else phone.</param>
/// <param name="SchoolNames">When set, used as the school-name text in the welcome message
/// (comma-joined) instead of looking up the current tenant's own name — lets a multi-school
/// invite batch list every school in the single message it sends.</param>
/// <param name="Message">Optional personal note from the inviter, shown in the welcome email
/// above the login link/code.</param>
public sealed record InviteUserRequest(
    string? Email, string? Phone, string[] Roles,
    bool SendWelcome = true, string Method = "code", string? Channel = null, string[]? SchoolNames = null,
    string? Message = null);
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
public sealed record SetUserActiveRequest(bool Active);
