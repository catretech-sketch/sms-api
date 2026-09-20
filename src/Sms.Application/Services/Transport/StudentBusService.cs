using Sms.Application.Common;
using Sms.Application.Interfaces.DAO;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Transport;

public interface IStudentBusService
{
    Task<ApiResult> AssignAsync(Guid busId, Guid studentId, Guid? stopId, CancellationToken ct = default);
    Task<ApiResult> UnassignAsync(Guid studentId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<StudentBusAssignmentResponse>>> ListByBusAsync(Guid busId, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<TransportMappedStudentResponse>>> ListMappedAsync(
        TransportStudentsFilter filter, CancellationToken ct = default);

    /// Parent/student app: live bus for the caller's own linked students. Never accepts a student
    /// id from the client. <paramref name="selfOnly"/> pins a student login to that one roster row
    /// so siblings linked to the same user as a parent are never returned.
    Task<ApiResult<IReadOnlyList<ChildBusPositionResponse>>> GetMyChildrenBusAsync(
        CancellationToken ct = default, bool selfOnly = false);
}

public sealed class StudentBusService(
    StudentBusRepository repo, BusRepository busRepo, IAuthDao users, ITenantContext tenant, ITenantFeatureSet features, IClock clock)
    : IStudentBusService
{
    private bool GpsAllowed => FeatureGate.Allowed(tenant, features, FeatureCatalog.TransportGps);
    public async Task<ApiResult> AssignAsync(Guid busId, Guid studentId, Guid? stopId, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await repo.BusExistsAsync(busId, ct))
            return ApiResult.Fail(new Error("not_found", "bus not found"), 404);
        if (!await repo.StudentExistsAsync(studentId, ct))
            return ApiResult.Fail(new Error("not_found", "student not found"), 404);

        var (capacity, occupied) = await busRepo.GetCapacityAndOccupancyAsync(busId, ct);
        if (capacity is int cap && occupied >= cap && !await repo.IsStudentOnBusAsync(studentId, busId, ct))
            return ApiResult.Fail(new Error("capacity_reached", $"Bus capacity reached ({occupied}/{cap})"), 409);

        await repo.AssignAsync(tid, studentId, busId, stopId, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult> UnassignAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        await repo.UnassignAsync(tid, studentId, ct);
        return ApiResult.NoContent();
    }

    public async Task<ApiResult<IReadOnlyList<StudentBusAssignmentResponse>>> ListByBusAsync(Guid busId, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<IReadOnlyList<StudentBusAssignmentResponse>>(FeatureCatalog.Operations);
        return ApiResult<IReadOnlyList<StudentBusAssignmentResponse>>.Ok(await repo.ListByBusAsync(busId, ct));
    }

    public async Task<ApiResult<IReadOnlyList<TransportMappedStudentResponse>>> ListMappedAsync(
        TransportStudentsFilter filter, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<IReadOnlyList<TransportMappedStudentResponse>>(FeatureCatalog.Operations);

        // "assigned" is the status vocabulary used by the single-student transport endpoint for the same
        // underlying concept as this list's "mapped" — accept it as a synonym rather than silently
        // returning zero rows, since it's the exact string a caller is likely to guess.
        switch (filter.Status)
        {
            case null or "mapped" or "pending":
                break;
            case "assigned":
                filter = filter with { Status = "mapped" };
                break;
            default:
                return ApiResult<IReadOnlyList<TransportMappedStudentResponse>>.Fail(
                    new Error("validation_error", $"Unknown status filter: {filter.Status}"), 400);
        }

        return ApiResult<IReadOnlyList<TransportMappedStudentResponse>>.Ok(await repo.ListMappedAsync(filter, ct));
    }

    public async Task<ApiResult<IReadOnlyList<ChildBusPositionResponse>>> GetMyChildrenBusAsync(
        CancellationToken ct = default, bool selfOnly = false)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Fail(new Error("forbidden", "no user context"), 403);

        IReadOnlyList<ChildBusRow> rows;
        if (selfOnly)
        {
            var me = await users.GetByIdAsync(uid, ct);
            if (me?.StudentId is not { Length: > 0 } admissionNo)
                return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Ok([]);
            rows = await repo.ChildrenBusByAdmissionAsync(admissionNo, ct);
        }
        else
        {
            var linkedIds = await repo.ListLinkedStudentIdsAsync(uid, ct);
            if (linkedIds.Count > 0)
            {
                rows = await repo.ChildrenBusByStudentIdsAsync(linkedIds, ct);
            }
            else
            {
                // Legacy parent login: Users.StudentId holds the child's admission number.
                var me = await users.GetByIdAsync(uid, ct);
                if (me?.StudentId is not { Length: > 0 } admissionNo)
                    return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Ok([]);
                rows = await repo.ChildrenBusByAdmissionAsync(admissionNo, ct);
            }
        }

        var now = clock.UtcNow;
        var stopsByBus = new Dictionary<Guid, IReadOnlyList<BusStopResponse>>();
        var stopsByRoute = new Dictionary<Guid, IReadOnlyList<BusStopResponse>>();
        var positionByBus = new Dictionary<Guid, BusPositionResponse>();
        var list = new List<ChildBusPositionResponse>(rows.Count);
        foreach (var r in rows)
        {
            var derived = BusTrackingStatusRules.Derive(
                now, r.LastPingAt, r.SpeedKmh, r.TripId is not null, GpsAllowed);
            double? lat = r.Lat;
            double? lng = r.Lng;
            double? speed = r.SpeedKmh;
            DateTime? lastPing = r.LastPingAt;
            if (!GpsAllowed)
            {
                lat = null;
                lng = null;
                speed = null;
                lastPing = null;
            }

            IReadOnlyList<BusStopResponse> routeStops = [];
            if (r.BusId is { } busId)
            {
                if (!stopsByBus.TryGetValue(busId, out var busStops))
                {
                    busStops = await busRepo.ListStopsForBusAsync(busId, ct);
                    stopsByBus[busId] = busStops;
                }
                routeStops = busStops;
            }
            else if (r.RouteId is { } routeId)
            {
                if (!stopsByRoute.TryGetValue(routeId, out var routeOnlyStops))
                {
                    routeOnlyStops = await busRepo.ListStopsForRouteAsync(routeId, ct);
                    stopsByRoute[routeId] = routeOnlyStops;
                }
                routeStops = routeOnlyStops;
            }

            string? nextStop = null;
            int? eta = null;
            int? currentStopIndex = null;
            string? currentStopName = null;
            int? passedStopCount = null;
            if (GpsAllowed && r.BusId is { } posBusId && r.TripId is not null && r.LastPingAt is not null)
            {
                if (!positionByBus.TryGetValue(posBusId, out var pos))
                {
                    pos = await busRepo.GetPositionAsync(posBusId, ct);
                    positionByBus[posBusId] = pos;
                }
                nextStop = pos.NextStopName;
                currentStopIndex = pos.CurrentStopIndex;
                if (currentStopIndex is int idx && idx >= 0 && idx < routeStops.Count)
                    currentStopName = routeStops[idx].Name;
                passedStopCount = currentStopIndex;
                if (derived.Tracking == BusTrackingStatusRules.Live)
                    eta = pos.EtaMinutes;
            }

            double? distanceM = null;
            if (lat is { } busLat && lng is { } busLng && r.StopLat is { } stopLat && r.StopLng is { } stopLng)
                distanceM = Math.Round(TripRepository.Haversine(busLat, busLng, stopLat, stopLng), 0);

            int? etaToStudentStop = null;
            if (derived.Tracking == BusTrackingStatusRules.Live
                && speed is > 1
                && distanceM is > 0)
            {
                etaToStudentStop = (int)Math.Ceiling((distanceM.Value / 1000.0) / speed.Value * 60.0);
            }

            list.Add(new ChildBusPositionResponse(
                r.StudentId, r.StudentName, r.AdmissionNo, r.BusId, r.BusNo, r.RouteName, derived.Legacy,
                lat, lng, speed, nextStop, lastPing,
                r.StopId, r.StopName, r.StopLat, r.StopLng, distanceM, routeStops,
                r.Grade, r.Section, r.Driver, r.DriverPhone,
                derived.Tracking, derived.Motion,
                BusTrackingStatusRules.Assignment(r.OptedOut != 0, r.AssignmentId, r.BusId),
                r.BoardingState, eta, currentStopIndex, currentStopName, passedStopCount, routeStops.Count,
                etaToStudentStop, r.RouteId));
        }
        return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Ok(list);
    }
}
