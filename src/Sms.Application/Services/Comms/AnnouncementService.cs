using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface IAnnouncementService
{
    Task<ApiResult<IReadOnlyList<AnnouncementResponse>>> ListAsync(string? audience, CancellationToken ct = default);
    Task<ApiResult<AnnouncementResponse>> CreateAsync(CreateAnnouncementRequest req, string? role, CancellationToken ct = default);
}

public sealed class AnnouncementService(CommsRepository repo, ITenantContext tenant) : IAnnouncementService
{
    public async Task<ApiResult<IReadOnlyList<AnnouncementResponse>>> ListAsync(string? audience, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<AnnouncementResponse>>.Ok(await repo.ListAnnouncementsAsync(audience, ct));

    public async Task<ApiResult<AnnouncementResponse>> CreateAsync(CreateAnnouncementRequest req, string? role, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<AnnouncementResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<AnnouncementResponse>.Ok((await repo.CreateAnnouncementAsync(tid, req, role, role, ct))!, 201);
    }
}
