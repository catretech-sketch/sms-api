using Sms.Application.Common;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Transport;

public interface ITripService
{
    Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default);
    Task<ApiResult<TripResponse?>> GetCurrentAsync(CancellationToken ct = default);
    Task<ApiResult> IngestPingsAsync(Guid tripId, BulkPingRequest req, CancellationToken ct = default);
    Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<BoardingResponse>>> ListBoardingAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default);
}

public sealed class TripService(TripRepository repo, ITenantContext tenant) : ITripService
{
    public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        return ApiResult<TripResponse>.Ok((await repo.StartAsync(tid, uid, req, ct))!, 201);
    }

    public async Task<ApiResult<TripResponse?>> GetCurrentAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<TripResponse?>.Fail(new Error("forbidden", "no user context"), 403);
        return ApiResult<TripResponse?>.Ok(await repo.GetCurrentAsync(uid, ct));
    }

    public async Task<ApiResult> IngestPingsAsync(Guid tripId, BulkPingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        await repo.IngestPingsAsync(tid, tripId, req.Pings, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default) =>
        ApiResult<TripSummaryResponse>.Ok(await repo.EndAsync(tripId, ct));

    public async Task<ApiResult<IReadOnlyList<BoardingResponse>>> ListBoardingAsync(Guid tripId, CancellationToken ct = default) =>
        ApiResult<IReadOnlyList<BoardingResponse>>.Ok(await repo.ListBoardingAsync(tripId, ct));

    public async Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        await repo.UpsertBoardingAsync(tid, tripId, req, ct);
        return ApiResult.NoContent();
    }
}
