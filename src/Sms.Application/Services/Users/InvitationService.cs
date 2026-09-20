using Sms.Application.Common;
using Sms.Application.DTOs.Users;
using Sms.Application.Interfaces.DAO;
using Sms.Application.Services.Auth;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Users;

public interface IInvitationService
{
    Task<ApiResult<IReadOnlyList<InvitationResponse>>> ListAsync(bool isSchoolAdmin, CancellationToken ct = default);
    Task<ApiResult<object>> ResendAsync(Guid id, bool isSchoolAdmin, CancellationToken ct = default);
    Task<ApiResult<object>> RevokeAsync(Guid id, bool isSchoolAdmin, CancellationToken ct = default);
}

public sealed class InvitationService(
    IInvitationDao invitations,
    IUserProvisioningDao users,
    IAuthDao authDao,
    ITenantContext tenant,
    IAuthService auth,
    ClientRepository clients) : IInvitationService
{
    public async Task<ApiResult<IReadOnlyList<InvitationResponse>>> ListAsync(bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<IReadOnlyList<InvitationResponse>>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<IReadOnlyList<InvitationResponse>>.Fail(new Error("forbidden", "no tenant context"), 403);

        var rows = await invitations.ListByTenantAsync(tid, ct);
        return ApiResult<IReadOnlyList<InvitationResponse>>.Ok(rows.Select(Map).ToList());
    }

    public async Task<ApiResult<object>> ResendAsync(Guid id, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<object>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<object>.Fail(new Error("forbidden", "no tenant context"), 403);

        var row = await invitations.GetByIdAsync(tid, id, ct);
        if (row is null)
            return ApiResult<object>.Fail(new Error("not_found", "invitation not found"), 404);
        if (row.AcceptedAt is not null || row.RevokedAt is not null)
            return ApiResult<object>.Fail(new Error("conflict", "invitation already accepted or revoked"), 409);

        var identifier = row.Email ?? row.Phone;
        if (string.IsNullOrWhiteSpace(identifier))
            return ApiResult<object>.Fail(new Error("invalid_request", "invitation has no email or phone"), 422);

        var school = await clients.GetAsync(tid, ct);
        var schoolName = school?.Name ?? "your school";
        await auth.SendInviteSetupAsync(identifier!, schoolName, row.RoleLabel, TimeSpan.FromHours(24), ct: ct);
        await invitations.MarkResentAsync(id, DateTime.UtcNow.AddHours(24), ct);
        return ApiResult<object>.Ok(new { resent = true });
    }

    public async Task<ApiResult<object>> RevokeAsync(Guid id, bool isSchoolAdmin, CancellationToken ct = default)
    {
        if (!isSchoolAdmin)
            return ApiResult<object>.Fail(new Error("forbidden", "school admin only"), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult<object>.Fail(new Error("forbidden", "no tenant context"), 403);

        var row = await invitations.GetByIdAsync(tid, id, ct);
        if (row is null)
            return ApiResult<object>.Fail(new Error("not_found", "invitation not found"), 404);
        if (row.AcceptedAt is not null || row.RevokedAt is not null)
            return ApiResult<object>.Fail(new Error("conflict", "invitation already accepted or revoked"), 409);

        await users.SetStatusAsync(row.UserId, "revoked", ct);
        var identifier = row.Email ?? row.Phone;
        if (!string.IsNullOrWhiteSpace(identifier))
            await authDao.OtpConsumeAllAsync(identifier!, ct);
        await invitations.MarkRevokedAsync(id, ct);
        return ApiResult<object>.Ok(new { revoked = true });
    }

    private static string ComputeStatus(InvitationRow r) =>
        r.RevokedAt is not null ? "revoked"
        : r.AcceptedAt is not null ? "accepted"
        : r.ExpiresAt < DateTime.UtcNow ? "expired"
        : "pending";

    private static InvitationResponse Map(InvitationRow r) =>
        new(r.Id, r.Email, r.Phone, r.RoleLabel, r.InvitedAt, r.ExpiresAt, ComputeStatus(r));
}
