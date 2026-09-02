using Sms.Application.Common;
using Sms.Modules.Staffing.Contracts;
using Sms.Modules.Staffing.Profile;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Profile;

public interface IProfileService
{
    Task<ApiResult<ProfileResponse>> GetAsync(CancellationToken ct = default);
}

public sealed class ProfileService(ProfileRepository repo, ITenantContext tenant) : IProfileService
{
    public async Task<ApiResult<ProfileResponse>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<ProfileResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var documents = await repo.ListForUserAsync(tid, uid, ct);
        return ApiResult<ProfileResponse>.Ok(new ProfileResponse(documents));
    }
}
