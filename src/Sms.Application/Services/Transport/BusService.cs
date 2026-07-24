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
    Task<ApiResult<TransportSummaryResponse>> GetSummaryAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FleetBusResponse>>> GetFleetAsync(CancellationToken ct = default);
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

    public async Task<ApiResult<TransportSummaryResponse>> GetSummaryAsync(CancellationToken ct = default) =>
        ApiResult<TransportSummaryResponse>.Ok(await repo.SummaryAsync(ct));

    public async Task<ApiResult<IReadOnlyList<FleetBusResponse>>> GetFleetAsync(CancellationToken ct = default)
    {
        var rows = await repo.FleetAsync(ct);
        var now = clock.UtcNow;
        var list = new List<FleetBusResponse>(rows.Count);
        foreach (var r in rows)
        {
            string status;
            string? nextStop = null;
            if (r.TripId is null || r.LastPingAt is null)
            {
                // No live trip (or a trip with no GPS yet) — vehicle is not actively tracked.
                status = "idle";
            }
            else
            {
                var ageMin = (now - r.LastPingAt.Value).TotalMinutes;
                // Stale telemetry ⇒ delayed; near-zero speed ⇒ stopped at a point; otherwise en route.
                status = ageMin > 5 ? "delayed"
                    : (r.SpeedKmh is <= 3) ? "at_stop"
                    : "on_route";
                nextStop = (await repo.GetPositionAsync(r.BusId, ct)).NextStopName;
            }

            list.Add(new FleetBusResponse(
                r.BusId, r.BusNo, r.RouteName, r.Driver, r.DriverPhone,
                r.StopCount, r.StudentsRiding, status,
                r.Lat, r.Lng, r.SpeedKmh, nextStop, r.LastPingAt));
        }
        return ApiResult<IReadOnlyList<FleetBusResponse>>.Ok(list);
    }

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
