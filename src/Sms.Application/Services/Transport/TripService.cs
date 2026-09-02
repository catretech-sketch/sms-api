using Sms.Application.Common;
using Sms.Application.Services.Realtime;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Transport;

public interface ITripService
{
    Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default);
    Task<ApiResult<TripResponse?>> GetCurrentAsync(CancellationToken ct = default);
    Task<ApiResult<StaffTripAssignmentResponse>> GetAssignmentAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<StaffRosterStudentResponse>>> GetRosterAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResult> IngestPingsAsync(Guid tripId, BulkPingRequest req, CancellationToken ct = default);
    Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<BoardingResponse>>> ListBoardingAsync(Guid tripId, CancellationToken ct = default);
    Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default);
}

/// Every mutation that changes a trip's live state (start/ping/end) also pushes a fleet snapshot
/// and a live event, matching the bus-duty lifecycle in BusService — otherwise a driver-started
/// trip would only ever be visible to pollers, defeating the point of "live" tracking.
public sealed class TripService(
    TripRepository repo, ITenantContext tenant,
    ITransportFleetBroadcaster fleetBroadcaster, ILiveBroadcaster live, IClock clock) : ITripService
{
    public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var trip = (await repo.StartAsync(tid, uid, req, ct))!;
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult<TripResponse>.Ok(WithActiveBroadcaster(trip), 201);
    }

    public async Task<ApiResult<TripResponse?>> GetCurrentAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<TripResponse?>.Fail(new Error("forbidden", "no user context"), 403);
        var trip = await repo.GetCurrentAsync(uid, ct);
        return ApiResult<TripResponse?>.Ok(trip is null ? null : WithActiveBroadcaster(trip));
    }

    public async Task<ApiResult> IngestPingsAsync(Guid tripId, BulkPingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var role = await repo.GetParticipantRoleAsync(tripId, uid, ct);
        if (role is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        await repo.IngestPingsAsync(tid, tripId, req.Pings, ct);
        await repo.MarkPingAsync(tripId, role, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "not your trip"), 403);
        var summary = await repo.EndAsync(tripId, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult<TripSummaryResponse>.Ok(summary);
    }

    public async Task<ApiResult<StaffTripAssignmentResponse>> GetAssignmentAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<StaffTripAssignmentResponse>.Fail(new Error("forbidden", "no user context"), 403);
        var assignment = await repo.GetAssignmentAsync(uid, ct);
        return assignment is null
            ? ApiResult<StaffTripAssignmentResponse>.Fail(new Error("not_found", "no assigned bus"), 404)
            : ApiResult<StaffTripAssignmentResponse>.Ok(assignment);
    }

    public async Task<ApiResult<IReadOnlyList<StaffRosterStudentResponse>>> GetRosterAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<StaffRosterStudentResponse>>.Fail(new Error("forbidden", "no user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<IReadOnlyList<StaffRosterStudentResponse>>.Fail(new Error("forbidden", "not your trip"), 403);
        return ApiResult<IReadOnlyList<StaffRosterStudentResponse>>.Ok(await repo.GetRosterAsync(tripId, ct));
    }

    public async Task<ApiResult<IReadOnlyList<BoardingResponse>>> ListBoardingAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<BoardingResponse>>.Fail(new Error("forbidden", "no user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<IReadOnlyList<BoardingResponse>>.Fail(new Error("forbidden", "not your trip"), 403);
        return ApiResult<IReadOnlyList<BoardingResponse>>.Ok(await repo.ListBoardingAsync(tripId, ct));
    }

    public async Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        await repo.UpsertBoardingAsync(tid, tripId, req, ct);
        return ApiResult.NoContent();
    }

    private TripResponse WithActiveBroadcaster(TripResponse trip) =>
        trip with { ActiveBroadcaster = TripBroadcasterRules.Compute(trip.DriverLastPingAt, trip.ConductorLastPingAt, clock.UtcNow) };
}
