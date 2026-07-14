using Sms.Application.Common;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Comms;

public interface IComplaintService
{
    Task<ApiResult<IReadOnlyList<ComplaintResponse>>> ListAsync(string? status, CancellationToken ct = default);
    Task<ApiResult<ComplaintResponse>> CreateAsync(CreateComplaintRequest req, CancellationToken ct = default);
    Task<ApiResult<ComplaintResponse>> UpdateAsync(Guid id, UpdateComplaintRequest req, CancellationToken ct = default);
}

public sealed class ComplaintService(CommsRepository repo, ITenantContext tenant) : IComplaintService
{
    public async Task<ApiResult<IReadOnlyList<ComplaintResponse>>> ListAsync(string? status, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<ComplaintResponse>>.Ok(await repo.ListComplaintsAsync(status, ct));

    public async Task<ApiResult<ComplaintResponse>> CreateAsync(CreateComplaintRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<ComplaintResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<ComplaintResponse>.Ok((await repo.CreateComplaintAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<ComplaintResponse>> UpdateAsync(Guid id, UpdateComplaintRequest req, CancellationToken ct = default)
    {
        if (await repo.GetComplaintAsync(id, ct) is null)
            return ApiResult<ComplaintResponse>.Fail(new Error("not_found", "resource not found"), 404);
        return ApiResult<ComplaintResponse>.Ok((await repo.UpdateComplaintAsync(id, req.Status, req.Assignee, ct))!);
    }
}
