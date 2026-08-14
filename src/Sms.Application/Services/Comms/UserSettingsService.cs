using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface IUserSettingsService
{
    Task<ApiResult<UserAppSettingsResponse>> GetAsync(CancellationToken ct = default);
    Task<ApiResult<UserAppSettingsResponse>> UpdateAsync(
        UpdateUserAppSettingsRequest req, CancellationToken ct = default);
}

public sealed class UserSettingsService(UserAppSettingsRepository repo, ITenantContext tenant) : IUserSettingsService
{
    public async Task<ApiResult<UserAppSettingsResponse>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<UserAppSettingsResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);
        return ApiResult<UserAppSettingsResponse>.Ok(await repo.GetOrDefaultAsync(uid, ct));
    }

    public async Task<ApiResult<UserAppSettingsResponse>> UpdateAsync(
        UpdateUserAppSettingsRequest req, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<UserAppSettingsResponse>.Fail(new Error("unauthorized", "unauthorized"), 401);
        if (tenant.TenantId is not { } tid)
            return ApiResult<UserAppSettingsResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var current = await repo.GetOrDefaultAsync(uid, ct);
        var next = new UserAppSettingsResponse(
            req.ChatAlerts ?? current.ChatAlerts,
            req.SchoolNotices ?? current.SchoolNotices,
            req.InAppToasts ?? current.InAppToasts);
        return ApiResult<UserAppSettingsResponse>.Ok(await repo.UpsertAsync(tid, uid, next, ct));
    }
}
