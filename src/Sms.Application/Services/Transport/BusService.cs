using Sms.Application.Common;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Application.Services.Realtime;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Transport;

public interface IBusService
{
    Task<ApiResult<BusResponse>> GetAssignedAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<BusRosterEntry>>> GetRosterAsync(Guid busId, CancellationToken ct = default);
    Task<ApiResult<BusPositionResponse>> GetPositionAsync(Guid busId, CancellationToken ct = default);
    Task<ApiResult> UpsertBoardingAsync(Guid busId, BusBoardingRequest req, CancellationToken ct = default);
    Task<ApiResult<TransportSummaryResponse>> GetSummaryAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FleetBusResponse>>> GetFleetAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TransportBusResponse>>> ListBusesAsync(CancellationToken ct = default);
    Task<ApiResult<BusTeacherAssignmentResponse>> AssignTeacherAsync(Guid busId, Guid teacherUserId, CancellationToken ct = default);
    Task<ApiResult> UnassignTeacherAsync(Guid busId, CancellationToken ct = default);
    Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        Guid? conductorStaffId = null, CancellationToken ct = default);
    Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        Guid? conductorStaffId = null, bool clearConductor = false, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TransportRouteListItem>>> ListRoutesAsync(CancellationToken ct = default);
    Task<ApiResult<TransportRouteListItem>> CreateRouteAsync(string name, int stops, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<RouteStopListItem>>> ListRouteStopsAsync(Guid routeId, CancellationToken ct = default);
    Task<ApiResult<RouteStopListItem>> CreateRouteStopAsync(Guid routeId, string name, double lat, double lng, CancellationToken ct = default);
    Task<ApiResult<RouteStopListItem>> UpdateRouteStopAsync(Guid routeId, Guid stopId, string name, double lat, double lng, CancellationToken ct = default);
    Task<ApiResult> DeleteRouteStopAsync(Guid routeId, Guid stopId, CancellationToken ct = default);
    Task<ApiResult> ReorderRouteStopsAsync(Guid routeId, IReadOnlyList<Guid> stopIds, CancellationToken ct = default);
    Task<ApiResult<TripResponse>> StartBusTripAsync(Guid busId, string direction, CancellationToken ct = default);
    Task<ApiResult> IngestBusTripPingsAsync(Guid busId, BulkPingRequest req, CancellationToken ct = default);
    Task<ApiResult<TripSummaryResponse>> EndBusTripAsync(Guid busId, CancellationToken ct = default);
}

public sealed class BusService(
    BusRepository repo, TripRepository trips, ITenantContext tenant, ITenantFeatureSet features, IClock clock,
    FleetSnapshotBuilder fleet, ITransportFleetBroadcaster fleetBroadcaster, ILiveBroadcaster live) : IBusService
{
    private bool GpsAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.TransportGps);
    private bool OperationsAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations);

    public async Task<ApiResult<BusResponse>> GetAssignedAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<BusResponse>.Fail(new Error("forbidden", "no user context"), 403);
        var bus = await repo.GetAssignedAsync(uid, ct);
        return bus is null
            ? ApiResult<BusResponse>.Fail(new Error("not_found", "no assigned bus"), 404)
            : ApiResult<BusResponse>.Ok(bus);
    }

    public async Task<ApiResult<IReadOnlyList<BusRosterEntry>>> GetRosterAsync(Guid busId, CancellationToken ct = default)
    {
        if (await EnsureBusDutyAsync(busId, ct) is { } denied)
            return ApiResult<IReadOnlyList<BusRosterEntry>>.Fail(denied.Error!, denied.StatusCode);
        return ApiResult<IReadOnlyList<BusRosterEntry>>.Ok(await repo.GetRosterAsync(busId, ct));
    }

    public async Task<ApiResult<BusPositionResponse>> GetPositionAsync(Guid busId, CancellationToken ct = default)
    {
        if (!GpsAllowed) return FeatureGate.Locked<BusPositionResponse>(FeatureCatalog.TransportGps);
        if (await EnsureBusDutyAsync(busId, ct) is { } denied)
            return ApiResult<BusPositionResponse>.Fail(denied.Error!, denied.StatusCode);
        return ApiResult<BusPositionResponse>.Ok(await repo.GetPositionAsync(busId, ct));
    }

    public async Task<ApiResult<TransportSummaryResponse>> GetSummaryAsync(CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<TransportSummaryResponse>(FeatureCatalog.Operations);
        return ApiResult<TransportSummaryResponse>.Ok(await repo.SummaryAsync(ct));
    }

    public async Task<ApiResult<IReadOnlyList<FleetBusResponse>>> GetFleetAsync(CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<IReadOnlyList<FleetBusResponse>>(FeatureCatalog.Operations);
        return ApiResult<IReadOnlyList<FleetBusResponse>>.Ok(await fleet.BuildAsync(ct));
    }

    public async Task<ApiResult<IReadOnlyList<TransportBusResponse>>> ListBusesAsync(CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<IReadOnlyList<TransportBusResponse>>(FeatureCatalog.Operations);
        return ApiResult<IReadOnlyList<TransportBusResponse>>.Ok(await repo.ListBusesAsync(ct));
    }

    public async Task<ApiResult<BusTeacherAssignmentResponse>> AssignTeacherAsync(
        Guid busId, Guid teacherUserId, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<BusTeacherAssignmentResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<BusTeacherAssignmentResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult<BusTeacherAssignmentResponse>.Fail(new Error("not_found", "bus not found"), 404);
        await repo.AssignTeacherAsync(tid, busId, teacherUserId, ct);
        var row = await repo.GetTeacherAssignmentAsync(busId, ct);
        return row is null
            ? ApiResult<BusTeacherAssignmentResponse>.Fail(new Error("not_found", "bus not found"), 404)
            : ApiResult<BusTeacherAssignmentResponse>.Ok(row);
    }

    public async Task<ApiResult> UnassignTeacherAsync(Guid busId, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult.Fail(new Error("not_found", "bus not found"), 404);
        await repo.UnassignTeacherAsync(tid, busId, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<FleetBusResponse>> CreateBusAsync(
        string busNo, string? routeName, Guid? routeId, string? driver, string? driverPhone, Guid? driverStaffId,
        Guid? conductorStaffId = null, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<FleetBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<FleetBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var trimmed = busNo.Trim();
        if (trimmed.Length == 0)
            return ApiResult<FleetBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        if (conductorStaffId is Guid cid && !await repo.StaffExistsAsync(cid, ct))
            return ApiResult<FleetBusResponse>.Fail(new Error("not_found", "conductor staff not found"), 404);
        var row = await repo.CreateBusAsync(tid, trimmed, routeName?.Trim(), routeId, driver?.Trim(), driverPhone?.Trim(), driverStaffId, conductorStaffId, ct);
        if (row is null)
            return ApiResult<FleetBusResponse>.Fail(new Error("server_error", "could not create bus"), 500);
        return ApiResult<FleetBusResponse>.Ok(ToFleetBus(row), 201);
    }

    public async Task<ApiResult<TransportBusResponse>> UpdateBusAsync(
        Guid busId, string? busNo, Guid? routeId, Guid? driverStaffId, bool clearDriver,
        Guid? conductorStaffId = null, bool clearConductor = false, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<TransportBusResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<TransportBusResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        if (routeId is Guid rid && !await repo.RouteExistsAsync(rid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "route not found"), 404);
        if (driverStaffId is Guid sid && !await repo.StaffExistsAsync(sid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "driver staff not found"), 404);
        if (conductorStaffId is Guid cid && !await repo.StaffExistsAsync(cid, ct))
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "conductor staff not found"), 404);
        var trimmed = busNo?.Trim();
        if (trimmed is { Length: 0 })
            return ApiResult<TransportBusResponse>.Fail(new Error("validation", "bus number is required"), 400);
        var row = await repo.UpdateBusAsync(tid, busId, trimmed, routeId, driverStaffId, clearDriver, conductorStaffId, clearConductor, ct);
        if (row is null)
            return ApiResult<TransportBusResponse>.Fail(new Error("not_found", "bus not found"), 404);
        return ApiResult<TransportBusResponse>.Ok(new TransportBusResponse(
            row.BusId, row.BusNo, row.RouteId, row.RouteName, row.DriverStaffId, row.Driver, row.DriverPhone,
            row.StopCount, row.StudentsAssigned, null, null, row.ConductorStaffId));
    }

    public async Task<ApiResult<IReadOnlyList<TransportRouteListItem>>> ListRoutesAsync(CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<IReadOnlyList<TransportRouteListItem>>(FeatureCatalog.Operations);
        return ApiResult<IReadOnlyList<TransportRouteListItem>>.Ok(await repo.ListRoutesAsync(ct));
    }

    public async Task<ApiResult<TransportRouteListItem>> CreateRouteAsync(string name, int stops, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<TransportRouteListItem>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<TransportRouteListItem>.Fail(new Error("forbidden", "no tenant context"), 403);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return ApiResult<TransportRouteListItem>.Fail(new Error("validation", "route name is required"), 400);
        var row = await repo.CreateRouteAsync(tid, trimmed, Math.Max(1, stops), ct);
        if (row is null)
            return ApiResult<TransportRouteListItem>.Fail(new Error("server_error", "could not create route"), 500);
        return ApiResult<TransportRouteListItem>.Ok(row, 201);
    }

    public async Task<ApiResult<IReadOnlyList<RouteStopListItem>>> ListRouteStopsAsync(Guid routeId, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<IReadOnlyList<RouteStopListItem>>(FeatureCatalog.Operations);
        if (!await repo.RouteExistsAsync(routeId, ct))
            return ApiResult<IReadOnlyList<RouteStopListItem>>.Fail(new Error("not_found", "route not found"), 404);
        return ApiResult<IReadOnlyList<RouteStopListItem>>.Ok(await repo.ListRouteStopsAsync(routeId, ct));
    }

    public async Task<ApiResult<RouteStopListItem>> CreateRouteStopAsync(
        Guid routeId, string name, double lat, double lng, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<RouteStopListItem>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<RouteStopListItem>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.RouteExistsAsync(routeId, ct))
            return ApiResult<RouteStopListItem>.Fail(new Error("not_found", "route not found"), 404);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return ApiResult<RouteStopListItem>.Fail(new Error("validation", "stop name is required"), 400);
        var row = await repo.CreateRouteStopAsync(tid, routeId, trimmed, lat, lng, ct);
        return row is null
            ? ApiResult<RouteStopListItem>.Fail(new Error("server_error", "could not create stop"), 500)
            : ApiResult<RouteStopListItem>.Ok(row, 201);
    }

    public async Task<ApiResult<RouteStopListItem>> UpdateRouteStopAsync(
        Guid routeId, Guid stopId, string name, double lat, double lng, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked<RouteStopListItem>(FeatureCatalog.Operations);
        if (!await repo.RouteExistsAsync(routeId, ct))
            return ApiResult<RouteStopListItem>.Fail(new Error("not_found", "route not found"), 404);
        if (!await repo.RouteStopExistsAsync(stopId, ct))
            return ApiResult<RouteStopListItem>.Fail(new Error("not_found", "stop not found"), 404);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return ApiResult<RouteStopListItem>.Fail(new Error("validation", "stop name is required"), 400);
        var row = await repo.UpdateRouteStopAsync(stopId, trimmed, lat, lng, ct);
        return row is null
            ? ApiResult<RouteStopListItem>.Fail(new Error("not_found", "stop not found"), 404)
            : ApiResult<RouteStopListItem>.Ok(row);
    }

    public async Task<ApiResult> DeleteRouteStopAsync(Guid routeId, Guid stopId, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked(FeatureCatalog.Operations);
        if (!await repo.RouteExistsAsync(routeId, ct))
            return ApiResult.Fail(new Error("not_found", "route not found"), 404);
        if (!await repo.RouteStopExistsAsync(stopId, ct))
            return ApiResult.Fail(new Error("not_found", "stop not found"), 404);
        if (!await repo.DeleteRouteStopAsync(routeId, stopId, ct))
            return ApiResult.Fail(new Error("not_found", "stop not found"), 404);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> ReorderRouteStopsAsync(Guid routeId, IReadOnlyList<Guid> stopIds, CancellationToken ct = default)
    {
        if (!OperationsAllowed) return FeatureGate.Locked(FeatureCatalog.Operations);
        if (!await repo.RouteExistsAsync(routeId, ct))
            return ApiResult.Fail(new Error("not_found", "route not found"), 404);
        await repo.ReorderRouteStopsAsync(routeId, stopIds, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripResponse>> StartBusTripAsync(Guid busId, string direction, CancellationToken ct = default)
    {
        if (!GpsAllowed) return FeatureGate.Locked<TripResponse>(FeatureCatalog.TransportGps);
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TripResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var ctx = await repo.GetBusTripContextAsync(busId, ct);
        if (ctx is null)
            return ApiResult<TripResponse>.Fail(new Error("not_found", "bus not found"), 404);
        var dir = string.IsNullOrWhiteSpace(direction) ? "pickup" : direction.Trim();
        var trip = await trips.StartAsync(tid, uid, new StartTripRequest(ctx.RouteId, ctx.BusNo, dir), ct);
        if (trip is null)
            return ApiResult<TripResponse>.Fail(new Error("server_error", "could not start trip"), 500);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult<TripResponse>.Ok(trip, 201);
    }

    public async Task<ApiResult> IngestBusTripPingsAsync(Guid busId, BulkPingRequest req, CancellationToken ct = default)
    {
        if (!GpsAllowed) return FeatureGate.Locked(FeatureCatalog.TransportGps);
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (req.Pings.Count == 0) return ApiResult.NoContent();
        var tripId = await repo.GetLiveTripIdForBusAsync(busId, ct);
        if (tripId is null)
            return ApiResult.Fail(new Error("no_active_trip", "no live trip for this bus"), 409);
        await trips.IngestPingsAsync(tid, tripId.Value, req.Pings, ct);
        await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
        await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<TripSummaryResponse>> EndBusTripAsync(Guid busId, CancellationToken ct = default)
    {
        if (!GpsAllowed) return FeatureGate.Locked<TripSummaryResponse>(FeatureCatalog.TransportGps);
        var tripId = await repo.GetLiveTripIdForBusAsync(busId, ct);
        if (tripId is null)
            return ApiResult<TripSummaryResponse>.Fail(new Error("no_active_trip", "no live trip for this bus"), 409);
        var summary = await trips.EndAsync(tripId.Value, ct);
        if (tenant.TenantId is { } tid)
        {
            await fleetBroadcaster.BroadcastFleetAsync(tid, ct);
            await live.PublishAsync(tid, LiveEventTypes.Transport, ct: ct);
        }
        return ApiResult<TripSummaryResponse>.Ok(summary);
    }

    public async Task<ApiResult> UpsertBoardingAsync(Guid busId, BusBoardingRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (await EnsureBusDutyAsync(busId, ct) is { } denied) return denied;
        var ok = await repo.UpsertBoardingAsync(tid, busId, req.Records, clock.UtcNow, ct);
        return ok
            ? ApiResult.NoContent()
            : ApiResult.Fail(new Error("no_active_trip", "no live trip for this bus"), 409);
    }

    private static FleetBusResponse ToFleetBus(CreatedBusRow r) =>
        new(r.BusId, r.RouteId, r.BusNo, r.RouteName, r.Driver, r.DriverPhone,
            r.StopCount, r.StudentsRiding, r.Status,
            null, null, null, null, null);

    /// Duty teachers may only access the bus they are assigned to via BusAssignments.
    private async Task<ApiResult?> EnsureBusDutyAsync(Guid busId, CancellationToken ct)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult.Fail(new Error("forbidden", "no user context"), 403);
        var assigned = await repo.GetAssignedAsync(uid, ct);
        if (assigned is null || assigned.Id != busId)
            return ApiResult.Fail(new Error("forbidden", "not assigned to this bus"), 403);
        return null;
    }
}
