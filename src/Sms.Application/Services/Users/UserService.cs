using Sms.Application.Common;
using Sms.Application.DTOs.Users;
using Sms.Application.Interfaces.DAO;
using Sms.Application.Services.Auth;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Users;

public interface IUserService
{
    /// <param name="canAssignOwner">True when inviter is school.owner (may assign co-owner).</param>
    Task<ApiResult<object>> InviteAsync(InviteUserRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default);
    Task<ApiResult<ImportResponse>> ImportAsync(ImportUsersRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<SchoolUserResponse>>> ListAsync(bool isSchoolAdmin, CancellationToken ct = default);
    Task<ApiResult<SchoolUserResponse>> SetRolesAsync(Guid userId, SetUserRolesRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<PermissionOverrideDto>>> GetPermissionsAsync(Guid userId, bool isSchoolAdmin, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<PermissionOverrideDto>>> SetPermissionsAsync(Guid userId, SetUserPermissionsRequest req, bool isSchoolAdmin, CancellationToken ct = default);
}

public sealed class UserService(
    IUserProvisioningDao dao,
    ITenantContext tenant,
    IAuthService auth,
    ClientRepository clients,
    IInvitationDao invitations) : IUserService
{
    /// School base roles: admin, principal, teacher (+ staff/parent). Owner only via school.owner inviter.
    private static readonly HashSet<string> BaseAssignableRoles = new(
        Policies.All.Where(r => r is not (Policies.PlatformOnly or Policies.SchoolOwner)),
        StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> AssignableFor(bool canAssignOwner)
    {
        if (!canAssignOwner) return BaseAssignableRoles;
        var set = new HashSet<string>(BaseAssignableRoles, StringComparer.OrdinalIgnoreCase) { Policies.SchoolOwner };
        return set;
    }

    public async Task<ApiResult<object>> InviteAsync(
        InviteUserRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<object>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<object>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (req.Email is null && req.Phone is null)
            return ApiResult<object>.Fail(new Error("invalid_request", "email or phone required"), 422);

        var allowed = AssignableFor(canAssignOwner);
        if (req.Roles.Length == 0 || req.Roles.Any(r => !allowed.Contains(r)))
            return ApiResult<object>.Fail(
                new Error("invalid_request",
                    canAssignOwner
                        ? "invalid role(s); use school.owner, school.admin, school.principal, school.teacher, staff, or student.parent"
                        : "invalid role(s); school.owner can only be assigned by a school owner"),
                422);

        var id = await dao.CreateUserAsync(tid, req.Email, req.Phone, false, req.Roles, ct);
        await dao.SetStatusAsync(id, "pending", ct);

        var roleLabel = RoleLabel(req.Roles.FirstOrDefault());
        await invitations.CreateAsync(tid, id, req.Email, req.Phone, roleLabel ?? "Member",
            tenant.UserId, DateTime.UtcNow.AddHours(24), ct);

        /* Welcome onboard email/SMS: school name + password-setup OTP (same reset flow). */
        var inviteId = req.Email ?? req.Phone;
        if (!string.IsNullOrWhiteSpace(inviteId))
        {
            var school = await clients.GetAsync(tid, ct);
            var schoolName = school?.Name ?? "your school";
            try { await auth.SendInviteSetupAsync(inviteId!, schoolName, roleLabel, TimeSpan.FromHours(24), ct); }
            catch { /* user row exists; invite email is best-effort */ }
        }
        return ApiResult<object>.Ok(new { id, invited = true }, 201);
    }

    private static string? RoleLabel(string? role) => role?.ToLowerInvariant() switch
    {
        Policies.SchoolOwner => "Owner",
        Policies.SchoolAdmin => "Admin",
        Policies.Principal => "Principal",
        "school.vice_principal" or "school.vice-principal" => "Vice-Principal",
        Policies.Teacher => "Teacher",
        Policies.Staff => "Staff",
        _ => role,
    };

    public async Task<ApiResult<ImportResponse>> ImportAsync(
        ImportUsersRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<ImportResponse>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<ImportResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var allowed = AssignableFor(canAssignOwner);
        var valid = new List<ImportRow>();
        var errors = new List<ImportError>();
        for (var i = 0; i < req.Rows.Length; i++)
        {
            var r = req.Rows[i];
            if (r.Email is null && r.Phone is null)
                errors.Add(new ImportError(i, "email or phone required"));
            else if (r.Role is not null && !allowed.Contains(r.Role))
                errors.Add(new ImportError(i, $"invalid role '{r.Role}'"));
            else
                valid.Add(new ImportRow(r.Email, r.Phone, r.Role));
        }

        var result = await dao.BulkCreateAsync(tid, valid, ct);
        return ApiResult<ImportResponse>.Ok(new ImportResponse(result.Created, result.Skipped, errors));
    }

    public async Task<ApiResult<IReadOnlyList<SchoolUserResponse>>> ListAsync(bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<IReadOnlyList<SchoolUserResponse>>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<SchoolUserResponse>>.Fail(new Error("forbidden", "no tenant context"), 403);

        var rows = await dao.ListByTenantAsync(tid, ct);
        var list = rows.Select(MapUser).ToList();
        return ApiResult<IReadOnlyList<SchoolUserResponse>>.Ok(list);
    }

    public async Task<ApiResult<SchoolUserResponse>> SetRolesAsync(
        Guid userId, SetUserRolesRequest req, bool isSchoolAdmin, bool canAssignOwner, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<SchoolUserResponse>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<SchoolUserResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await dao.UserInTenantAsync(userId, tid, ct))
            return ApiResult<SchoolUserResponse>.Fail(new Error("not_found", "user not found in this school"), 404);

        var allowed = AssignableFor(canAssignOwner);
        if (req.Roles.Length == 0 || req.Roles.Any(r => !allowed.Contains(r)))
            return ApiResult<SchoolUserResponse>.Fail(new Error("invalid_request", "invalid role(s) for this actor"), 422);

        await dao.ReplaceRolesAsync(userId, req.Roles, ct);
        var row = (await dao.ListByTenantAsync(tid, ct)).FirstOrDefault(u => u.Id == userId);
        return row is null
            ? ApiResult<SchoolUserResponse>.Fail(new Error("not_found", "user not found"), 404)
            : ApiResult<SchoolUserResponse>.Ok(MapUser(row));
    }

    public async Task<ApiResult<IReadOnlyList<PermissionOverrideDto>>> GetPermissionsAsync(
        Guid userId, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await dao.UserInTenantAsync(userId, tid, ct))
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("not_found", "user not found in this school"), 404);

        var rows = await dao.GetPermissionsAsync(userId, ct);
        return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Ok(rows);
    }

    public async Task<ApiResult<IReadOnlyList<PermissionOverrideDto>>> SetPermissionsAsync(
        Guid userId, SetUserPermissionsRequest req, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await dao.UserInTenantAsync(userId, tid, ct))
            return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Fail(new Error("not_found", "user not found in this school"), 404);

        var cleaned = (req.Overrides ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Module)
                        && o.Cap is "V" or "E" or "A"
                        && o.Effect is "grant" or "revoke")
            .Select(o => new PermissionOverrideDto(o.Module.Trim().ToLowerInvariant(), o.Cap, o.Effect))
            .ToList();

        await dao.SetPermissionsAsync(userId, cleaned, ct);
        return ApiResult<IReadOnlyList<PermissionOverrideDto>>.Ok(await dao.GetPermissionsAsync(userId, ct));
    }

    private static SchoolUserResponse MapUser(SchoolUserListRow r) =>
        new(r.Id, r.Email, r.Phone, r.Status, r.CreatedAt,
            string.IsNullOrWhiteSpace(r.Roles)
                ? []
                : r.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
