using Sms.Application.Common;
using Sms.Application.DTOs.Users;
using Sms.Application.Interfaces.DAO;
using Sms.Shared.Kernel.Auth;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Users;

public interface IUserService
{
    Task<ApiResult<object>> InviteAsync(InviteUserRequest req, bool isSchoolAdmin, CancellationToken ct = default);
    Task<ApiResult<ImportResponse>> ImportAsync(ImportUsersRequest req, bool isSchoolAdmin, CancellationToken ct = default);
}

public sealed class UserService(IUserProvisioningDao dao, ITenantContext tenant) : IUserService
{
    private static readonly HashSet<string> AssignableRoles = new(
        Policies.All.Where(r => r != Policies.PlatformOnly && r != Policies.SchoolOwner),
        StringComparer.OrdinalIgnoreCase);

    public async Task<ApiResult<object>> InviteAsync(InviteUserRequest req, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<object>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<object>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (req.Email is null && req.Phone is null)
            return ApiResult<object>.Fail(new Error("invalid_request", "email or phone required"), 422);
        if (req.Roles.Length == 0 || req.Roles.Any(r => !AssignableRoles.Contains(r)))
            return ApiResult<object>.Fail(new Error("invalid_request", "invalid role(s)"), 422);

        var id = await dao.CreateUserAsync(tid, req.Email, req.Phone, false, req.Roles, ct);
        return ApiResult<object>.Ok(new { id }, 201);
    }

    public async Task<ApiResult<ImportResponse>> ImportAsync(ImportUsersRequest req, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<ImportResponse>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<ImportResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var valid = new List<ImportRow>();
        var errors = new List<ImportError>();
        for (var i = 0; i < req.Rows.Length; i++)
        {
            var r = req.Rows[i];
            if (r.Email is null && r.Phone is null)
                errors.Add(new ImportError(i, "email or phone required"));
            else if (r.Role is not null && !AssignableRoles.Contains(r.Role))
                errors.Add(new ImportError(i, $"invalid role '{r.Role}'"));
            else
                valid.Add(new ImportRow(r.Email, r.Phone, r.Role));
        }

        var result = await dao.BulkCreateAsync(tid, valid, ct);
        return ApiResult<ImportResponse>.Ok(new ImportResponse(result.Created, result.Skipped, errors));
    }
}
