using Microsoft.Extensions.Configuration;
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
    Task<ApiResult> ConfirmStopArrivalAsync(Guid tripId, Guid stopId, CancellationToken ct = default);
    Task<ApiResult> CompleteStopAsync(Guid tripId, Guid stopId, CancellationToken ct = default);
    Task<ApiResult> MarkSchoolArrivedAsync(Guid tripId, CancellationToken ct = default);
}

/// Every mutation that changes a trip's live state (start/ping/end) also pushes a fleet snapshot
/// and a live event, matching the bus-duty lifecycle in BusService — otherwise a driver-started
/// trip would only ever be visible to pollers, defeating the point of "live" tracking.
public sealed class TripService(
    TripRepository repo, BusRepository buses, ITenantContext tenant,
    ITransportFleetBroadcaster fleetBroadcaster, ILiveBroadcaster live, IClock clock,
    IConfiguration config) : ITripService
{
    // Matches TransportOfflineSweepWorker's Math.Clamp-on-read convention for a config value
    // with a sane default and hard bounds, rather than trusting an unbounded/negative config
    // value straight through into the arrival check.
    private readonly double _arrivalRadiusMeters =
        Math.Clamp(config.GetValue<double?>("TransportStops:ArrivalRadiusMeters") ?? 100, 5, 1000);

    public async Task<ApiResult<TripResponse>> StartAsync(StartTripRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        // dbo.Trip_Start now returns no row (instead of inserting) when the bus it resolves
        // from req.BusNo already has a live trip — the guard has to live in the stored proc
        // because BusId is resolved there from BusNo, never in C#, so there is no busId this
        // service layer could check up front. A null result here means "blocked", not "trip
        // vanished immediately after insert" (that never happens), so it's safe to treat as 409.
        if (await repo.StartAsync(tid, uid, req, ct) is not { } trip)
            return ApiResult<TripResponse>.Fail(new Error("bus_already_active", "This bus already has an active trip"), 409);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        if (await repo.GetBusIdAsync(trip.Id, ct) is { } busId)
            await fleetBroadcaster.BroadcastTripStartedAsync(busId, trip.Id, trip.DriverId, trip.ConductorId, trip.Direction, trip.StartedAt ?? clock.UtcNow, ct);
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
        if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
        {
            var snapshot = await buses.GetLiveSnapshotAsync(busId, ct);
            var currentStopId = await repo.GetCurrentStopIdAsync(tripId, ct);
            // Only probe for a next stop when not already sitting at a confirmed one —
            // arrival detection targets the NEXT stop, not the current one (see
            // TripStopRepositoryTests' note that excluding the current stop is the caller's job).
            if (currentStopId is null && await repo.GetTripRouteIdAsync(tripId, ct) is { } routeId
                && await repo.GetNextIncompleteStopAsync(tripId, routeId, ct) is { } nextStop
                && snapshot.Lat is { } lat && snapshot.Lng is { } lng)
            {
                var distance = TripRepository.Haversine(lat, lng, nextStop.Lat, nextStop.Lng);
                var withinRadius = StopArrivalRules.IsWithinRadius(distance, _arrivalRadiusMeters);
                snapshot = snapshot with { NextStopId = nextStop.Id, WithinArrivalRadius = withinRadius, CurrentStopId = currentStopId };
            }
            else
            {
                snapshot = snapshot with { CurrentStopId = currentStopId };
            }
            await fleetBroadcaster.BroadcastPositionAsync(busId, snapshot, ct);
        }
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripSummaryResponse>> EndAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult<TripSummaryResponse>.Fail(new Error("forbidden", "not your trip"), 403);
        var busId = await repo.GetBusIdAsync(tripId, ct);
        var summary = await repo.EndAsync(tripId, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        if (busId is { } bid)
            await fleetBroadcaster.BroadcastTripEndedAsync(bid, tripId, clock.UtcNow, ct);
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

    private static readonly string[] ValidBoardingStates = ["boarded", "absent", "dropped"];

    public async Task<ApiResult> UpsertBoardingAsync(Guid tripId, BoardingRequest req, CancellationToken ct = default)
    {
        if (!ValidBoardingStates.Contains(req.State))
            return ApiResult.Fail(new Error("invalid_state", $"State must be one of: {string.Join(", ", ValidBoardingStates)}"), 400);
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        await repo.UpsertBoardingAsync(tid, tripId, req, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> ConfirmStopArrivalAsync(Guid tripId, Guid stopId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        if (await repo.GetCurrentStopIdAsync(tripId, ct) is { } current && current != stopId)
            return ApiResult.Fail(new Error("wrong_stop_order", "a different stop is already current"), 409);
        if (await repo.GetTripRouteIdAsync(tripId, ct) is not { } routeId)
            return ApiResult.Fail(new Error("no_route", "trip has no route"), 409);
        var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, ct);
        if (next is null || next.Id != stopId)
            return ApiResult.Fail(new Error("wrong_stop_order", "stops must be confirmed in sequence"), 409);

        await repo.ConfirmStopArrivalAsync(tid, tripId, stopId, next.Seq, clock.UtcNow, clock.UtcNow, ct);
        if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
            await fleetBroadcaster.BroadcastStopArrivedAsync(busId, tripId, stopId, next.Name, clock.UtcNow, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> CompleteStopAsync(Guid tripId, Guid stopId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        if (await repo.GetCurrentStopIdAsync(tripId, ct) != stopId)
            return ApiResult.Fail(new Error("not_current_stop", "this stop is not the confirmed current stop"), 409);

        await repo.CompleteStopAsync(tid, tripId, stopId, clock.UtcNow, ct);
        if (await repo.GetBusIdAsync(tripId, ct) is { } busId && await repo.GetTripRouteIdAsync(tripId, ct) is { } routeId)
        {
            var next = await repo.GetNextIncompleteStopAsync(tripId, routeId, ct);
            await fleetBroadcaster.BroadcastStopCompletedAsync(busId, tripId, stopId, next?.Id, next?.Name, clock.UtcNow, ct);
        }
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> MarkSchoolArrivedAsync(Guid tripId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (await repo.GetParticipantRoleAsync(tripId, uid, ct) is null)
            return ApiResult.Fail(new Error("forbidden", "not your trip"), 403);
        if (!await repo.IsPickupTripInProgressAsync(tripId, ct))
            return ApiResult.Fail(new Error("invalid_state", "not a pickup trip in progress"), 409);

        await repo.MarkSchoolArrivedAsync(tid, tripId, clock.UtcNow, ct);
        if (await repo.GetBusIdAsync(tripId, ct) is { } busId)
        {
            var onboard = await repo.CountBoardedAsync(tripId, ct);
            await fleetBroadcaster.BroadcastSchoolArrivedAsync(busId, tripId, clock.UtcNow, onboard, ct);
        }
        return ApiResult.NoContent();
    }

    private TripResponse WithActiveBroadcaster(TripResponse trip) =>
        trip with { ActiveBroadcaster = TripBroadcasterRules.Compute(trip.DriverLastPingAt, trip.ConductorLastPingAt, clock.UtcNow) };
}
