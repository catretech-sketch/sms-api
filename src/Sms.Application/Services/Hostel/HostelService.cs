using Sms.Application.Common;
using Sms.Modules.Hostel.Contracts;
using Sms.Modules.Hostel.Data;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Hostel;

public sealed class HostelService(HostelRepository repo, ITenantContext tenant) : IHostelService
{
    public async Task<ApiResult<HostelSummaryResponse>> GetSummaryAsync(CancellationToken ct = default) =>
        ApiResult<HostelSummaryResponse>.Ok(await repo.SummaryAsync(ct));

    public async Task<ApiResult<IReadOnlyList<HostelBlockResponse>>> ListBlocksAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<HostelBlockResponse>>.Ok(await repo.ListBlocksAsync(ct));

    public async Task<ApiResult<HostelBlockResponse>> CreateBlockAsync(CreateHostelBlockRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<HostelBlockResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.Name))
            return ApiResult<HostelBlockResponse>.Fail(new Error("bad_request", "block name is required"), 400);
        return ApiResult<HostelBlockResponse>.Ok((await repo.CreateBlockAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<HostelRoomResponse>>> ListRoomsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<HostelRoomResponse>>.Ok(await repo.ListRoomsAsync(ct));

    public async Task<ApiResult<HostelRoomResponse>> CreateRoomAsync(CreateHostelRoomRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<HostelRoomResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.RoomNo))
            return ApiResult<HostelRoomResponse>.Fail(new Error("bad_request", "room number is required"), 400);
        if (req.Capacity < 1)
            return ApiResult<HostelRoomResponse>.Fail(new Error("bad_request", "capacity must be at least 1"), 400);
        return ApiResult<HostelRoomResponse>.Ok((await repo.CreateRoomAsync(tid, req, ct))!, 201);
    }

    public async Task<ApiResult<IReadOnlyList<HostelResidentResponse>>> ListResidentsAsync(CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<HostelResidentResponse>>.Ok(await repo.ListResidentsAsync(ct));

    public async Task<ApiResult<HostelResidentResponse>> CreateResidentAsync(CreateHostelResidentRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<HostelResidentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (string.IsNullOrWhiteSpace(req.StudentName))
            return ApiResult<HostelResidentResponse>.Fail(new Error("bad_request", "student name is required"), 400);
        return ApiResult<HostelResidentResponse>.Ok((await repo.CreateResidentAsync(tid, req, ct))!, 201);
    }
}
