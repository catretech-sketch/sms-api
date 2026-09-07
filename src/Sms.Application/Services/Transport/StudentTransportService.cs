using Sms.Application.Common;
using Sms.Modules.Finance;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Transport;

public sealed record SetStudentTransportRequest(bool OptedIn, Guid? RouteId = null, Guid? StopId = null, Guid? FeeHeadId = null);
public sealed record StudentTransportPendingReason(string Code, string Message);
public sealed record StudentTransportResponse(
    bool OptedIn, bool Assigned, string Status, Guid? BusId, Guid? RouteId, Guid? StopId, Guid? FeeHeadId,
    StudentTransportPendingReason? PendingReason);

public interface IStudentTransportService
{
    Task<ApiResult<StudentTransportResponse>> SetAsync(Guid studentId, SetStudentTransportRequest req, CancellationToken ct = default);
    Task<ApiResult<StudentTransportResponse>> GetAsync(Guid studentId, CancellationToken ct = default);
}

public sealed class StudentTransportService(
    StudentBusRepository busAssignRepo, BusRepository busRepo, FeeHeadRepository feeHeadRepo, ITenantContext tenant,
    ITenantFeatureSet features)
    : IStudentTransportService
{
    private static readonly StudentTransportResponse NotMapped =
        new(false, false, "not_mapped", null, null, null, null, null);

    public async Task<ApiResult<StudentTransportResponse>> GetAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<StudentTransportResponse>(FeatureCatalog.Operations);
        if (!await busAssignRepo.StudentExistsAsync(studentId, ct))
            return ApiResult<StudentTransportResponse>.Fail(new Error("not_found", "student not found"), 404);

        var row = await busAssignRepo.GetTransportStatusAsync(studentId, ct);
        if (row is null)
            return ApiResult<StudentTransportResponse>.Ok(NotMapped);

        return ApiResult<StudentTransportResponse>.Ok(row.BusId is { } busId
            ? new StudentTransportResponse(true, true, "assigned", busId, row.RouteId, row.StopId, row.FeeHeadId, null)
            : new StudentTransportResponse(true, false, "pending", null, row.RouteId, row.StopId, row.FeeHeadId,
                new StudentTransportPendingReason("no_capacity", "No bus currently has available capacity on this route.")));
    }

    public async Task<ApiResult<StudentTransportResponse>> SetAsync(
        Guid studentId, SetStudentTransportRequest req, CancellationToken ct = default)
    {
        if (!FeatureGate.Allowed(tenant, features, FeatureCatalog.Operations))
            return FeatureGate.Locked<StudentTransportResponse>(FeatureCatalog.Operations);
        if (tenant.TenantId is not { } tid)
            return ApiResult<StudentTransportResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (!await busAssignRepo.StudentExistsAsync(studentId, ct))
            return ApiResult<StudentTransportResponse>.Fail(new Error("not_found", "student not found"), 404);

        if (!req.OptedIn)
        {
            await busAssignRepo.OptOutAsync(tid, studentId, ct);
            return ApiResult<StudentTransportResponse>.Ok(
                new StudentTransportResponse(false, false, "opted_out", null, null, null, null, null));
        }

        if (req.RouteId is not { } routeId)
            return ApiResult<StudentTransportResponse>.Fail(
                new Error("validation_error", "Route is required to opt in to transport"), 400);

        if (!await busRepo.RouteExistsAsync(routeId, ct))
            return ApiResult<StudentTransportResponse>.Fail(new Error("not_found", "route not found"), 404);

        if (req.StopId is { } stopId && !await busRepo.StopExistsAsync(stopId, ct))
            return ApiResult<StudentTransportResponse>.Fail(new Error("not_found", "stop not found"), 404);

        if (req.FeeHeadId is { } feeHeadId && !await feeHeadRepo.IsTransportFeeHeadAsync(feeHeadId, tid, ct))
            return ApiResult<StudentTransportResponse>.Fail(
                new Error("invalid_fee_head", "Selected fee head is not marked as the transport fee head"), 400);

        await busAssignRepo.OptInAsync(tid, studentId, ct);

        // The student may already hold a seat on one of these buses (e.g. re-saving the same route with a
        // different stop/fee head, or an idempotent retry). Don't let their own existing occupancy count against
        // themselves when scoring candidates, or a since-filled bus they already validly occupy would score as
        // full and evict them into "pending" for no real reason.
        var currentStatus = await busAssignRepo.GetTransportStatusAsync(studentId, ct);
        var currentBusId = currentStatus?.BusId;

        var candidates = await busRepo.ListBusesForRouteAsync(routeId, ct);
        Guid? busId = null;
        var bestFree = int.MinValue;
        foreach (var c in candidates)
        {
            int free;
            if (c.Capacity is not { } cap) { free = int.MaxValue; }
            else
            {
                var occupied = c.BusId == currentBusId ? c.Occupied - 1 : c.Occupied;
                free = cap - occupied;
                if (free <= 0) continue;
            }
            if (free > bestFree) { bestFree = free; busId = c.BusId; }
        }

        await busAssignRepo.UpsertTransportAsync(tid, studentId, routeId, req.StopId, req.FeeHeadId, busId, ct);

        if (busId is null)
            return ApiResult<StudentTransportResponse>.Ok(new StudentTransportResponse(
                true, false, "pending", null, routeId, req.StopId, req.FeeHeadId,
                new StudentTransportPendingReason("no_capacity", "No bus currently has available capacity on this route.")));

        return ApiResult<StudentTransportResponse>.Ok(
            new StudentTransportResponse(true, true, "assigned", busId, routeId, req.StopId, req.FeeHeadId, null));
    }
}
