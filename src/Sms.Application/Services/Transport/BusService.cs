using Sms.Application.Common;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Transport;

public interface IBusService
{
    Task<ApiResult<BusResponse>> GetAssignedAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<BusRosterEntry>>> GetRosterAsync(Guid busId, CancellationToken ct = default);
    Task<ApiResult<BusPositionResponse>> GetPositionAsync(Guid busId, CancellationToken ct = default);
    Task<ApiResult> UpsertBoardingAsync(Guid busId, BusBoardingRequest req, CancellationToken ct = default);
}

public sealed class BusService(BusRepository repo, ITenantContext tenant, IClock clock) : IBusService
{
    public async Task<ApiResult<BusResponse>> GetAssignedAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<BusResponse>.Fail(new Error("forbidden", "no user context"), 403);
        var bus = await repo.GetAssignedAsync(uid, ct);
        return bus is null
            ? ApiResult<BusResponse>.Fail(new Error("not_found", "no assigned bus"), 404)
            : ApiResult<BusResponse>.Ok(bus);
    }

    public async Task<ApiResult<IReadOnlyList<BusRosterEntry>>> GetRosterAsync(Guid busId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<BusRosterEntry>>.Ok(await repo.GetRosterAsync(busId, ct));

    public async Task<ApiResult<BusPositionResponse>> GetPositionAsync(Guid busId, CancellationToken ct = default) =>
        ApiResult<BusPositionResponse>.Ok(await repo.GetPositionAsync(busId, ct));

    public async Task<ApiResult> UpsertBoardingAsync(Guid busId, BusBoardingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        var ok = await repo.UpsertBoardingAsync(tid, busId, req.Records, clock.UtcNow, ct);
        return ok
            ? ApiResult.NoContent()
            : ApiResult.Fail(new Error("no_active_trip", "no live trip for this bus"), 409);
    }
}
