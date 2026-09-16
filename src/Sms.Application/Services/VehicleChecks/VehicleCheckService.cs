using System.Security.Claims;
using Sms.Application.Common;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Modules.Transport;
using Sms.Modules.VehicleChecks;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.VehicleChecks;

public interface IVehicleCheckService
{
    Task<ApiResult<InspectionResponse>> SubmitInspectionAsync(
        CreateInspectionRequest req, ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<InspectionResponse>>> ListInspectionsAsync(
        Guid busId, ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<FuelLogResponse>> SubmitFuelLogAsync(
        CreateFuelLogRequest req, ClaimsPrincipal caller, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<FuelLogResponse>>> ListFuelLogsAsync(
        Guid busId, ClaimsPrincipal caller, CancellationToken ct = default);
}

/// Driver/conductor-only Vehicle Check feature (inspections + fuel logs). Mirrors IssueService's
/// shape (manager notification via INotificationService) and TaskService's
/// "authorize against the caller's real identity, not a client-supplied id" convention, but the
/// authorization target here is a bus, not a task/issue row — see AuthorizeBusAccessAsync.
public sealed class VehicleCheckService(
    VehicleCheckRepository repo, TripRepository trips, INotificationService notifications,
    ITenantContext tenant, IClock clock) : IVehicleCheckService
{
    public async Task<ApiResult<InspectionResponse>> SubmitInspectionAsync(
        CreateInspectionRequest req, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (!RoleChecks.CanOperateTrips(caller))
            return ApiResult<InspectionResponse>.Fail(new Error("forbidden", "driver or conductor only"), 403);
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<InspectionResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (!await trips.IsDriverOrConductorAssignedToBusAsync(uid, req.BusId, ct))
            return ApiResult<InspectionResponse>.Fail(new Error("forbidden", "not assigned to this bus"), 403);

        var allOk = VehicleInspectionRules.ComputeAllOk(
            req.Brakes, req.Tyres, req.Lights, req.Horn,
            req.FirstAidKit, req.FireExtinguisher, req.EmergencyExit, req.FuelLevel);
        // Server-computed, school-local "today" — never client-supplied — is what the
        // one-per-bus-per-day upsert keys on, so a client can't backdate/forward-date its way
        // around the constraint.
        var inspectionDate = SchoolClock.ToSchoolLocal(clock.UtcNow).Date;

        var saved = await repo.UpsertInspectionAsync(tid, req.BusId, uid, req, allOk, inspectionDate, ct);
        if (saved is null)
            return ApiResult<InspectionResponse>.Fail(new Error("server_error", "could not save inspection"), 500);

        // A routine all-clear inspection is not notification-worthy; a failed one should stand
        // out to managers immediately, same "new issue" pattern as IssueService.CreateAsync.
        if (!allOk)
        {
            foreach (var managerId in await repo.GetManagerUserIdsAsync(tid, ct))
            {
                await notifications.CreateAsync(new CreateNotificationRequest(
                    Icon: "alert",
                    Tone: "warning",
                    Title: "Vehicle inspection failed",
                    Body: string.IsNullOrWhiteSpace(saved.Remarks)
                        ? "A vehicle inspection was submitted with one or more failed checks."
                        : saved.Remarks,
                    UserId: managerId), ct);
            }
        }

        return ApiResult<InspectionResponse>.Ok(saved);
    }

    public async Task<ApiResult<IReadOnlyList<InspectionResponse>>> ListInspectionsAsync(
        Guid busId, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        var authz = await AuthorizeBusAccessAsync(busId, caller, ct);
        if (authz.Error is { } error)
            return ApiResult<IReadOnlyList<InspectionResponse>>.Fail(error, authz.StatusCode);
        return ApiResult<IReadOnlyList<InspectionResponse>>.Ok(await repo.ListInspectionsAsync(busId, ct));
    }

    public async Task<ApiResult<FuelLogResponse>> SubmitFuelLogAsync(
        CreateFuelLogRequest req, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        if (!RoleChecks.CanOperateTrips(caller))
            return ApiResult<FuelLogResponse>.Fail(new Error("forbidden", "driver or conductor only"), 403);
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<FuelLogResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        if (req.OdometerKm < 0)
            return ApiResult<FuelLogResponse>.Fail(new Error("invalid_request", "OdometerKm must not be negative"), 400);
        if (req.FuelAddedLiters <= 0)
            return ApiResult<FuelLogResponse>.Fail(new Error("invalid_request", "FuelAddedLiters must be positive"), 400);
        if (!await trips.IsDriverOrConductorAssignedToBusAsync(uid, req.BusId, ct))
            return ApiResult<FuelLogResponse>.Fail(new Error("forbidden", "not assigned to this bus"), 403);

        var saved = await repo.CreateFuelLogAsync(tid, req.BusId, uid, req, ct);
        if (saved is null)
            return ApiResult<FuelLogResponse>.Fail(new Error("server_error", "could not save fuel log"), 500);
        return ApiResult<FuelLogResponse>.Ok(saved, 201);
    }

    public async Task<ApiResult<IReadOnlyList<FuelLogResponse>>> ListFuelLogsAsync(
        Guid busId, ClaimsPrincipal caller, CancellationToken ct = default)
    {
        var authz = await AuthorizeBusAccessAsync(busId, caller, ct);
        if (authz.Error is { } error)
            return ApiResult<IReadOnlyList<FuelLogResponse>>.Fail(error, authz.StatusCode);
        return ApiResult<IReadOnlyList<FuelLogResponse>>.Ok(await repo.ListFuelLogsAsync(busId, ct));
    }

    /// A bus's inspection/fuel-log history is visible to a manager (any bus in their tenant — RLS
    /// still confines this to their own tenant, so an unknown/cross-tenant id reads as a genuine
    /// 404) or to the driver/conductor actually assigned to that bus (403 otherwise, matching the
    /// submit-side gate exactly so "can I see it" and "can I submit to it" never disagree).
    private async Task<(Error? Error, int StatusCode)> AuthorizeBusAccessAsync(
        Guid busId, ClaimsPrincipal caller, CancellationToken ct)
    {
        if (tenant.UserId is not { } uid)
            return (new Error("forbidden", "no user context"), 403);

        if (RoleChecks.IsManagerTier(caller))
        {
            if (!await repo.BusExistsAsync(busId, ct))
                return (new Error("not_found", "resource not found"), 404);
            return (null, 200);
        }

        if (!await trips.IsDriverOrConductorAssignedToBusAsync(uid, busId, ct))
            return (new Error("forbidden", "not assigned to this bus"), 403);
        return (null, 200);
    }
}
