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

    /// Parent app: live bus position for the logged-in parent's child (or children). Tenant + child
    /// scoped — resolves the caller's linked student, never accepts a student id from the client.
    Task<ApiResult<IReadOnlyList<ChildBusPositionResponse>>> GetMyChildrenBusAsync(CancellationToken ct = default);
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

    public async Task<ApiResult<IReadOnlyList<ChildBusPositionResponse>>> GetMyChildrenBusAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Fail(new Error("forbidden", "no user context"), 403);

        // The parent's account is tied to a single student via Users.StudentId (their admission number).
        var me = await users.GetByIdAsync(uid, ct);
        if (me?.StudentId is not { Length: > 0 } admissionNo)
            return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Ok([]);

        var rows = await repo.ChildrenBusByAdmissionAsync(admissionNo, ct);
        var now = clock.UtcNow;
        var list = new List<ChildBusPositionResponse>(rows.Count);
        foreach (var r in rows)
        {
            string status;
            string? nextStop = null;
            double? lat = r.Lat;
            double? lng = r.Lng;
            double? speed = r.SpeedKmh;
            DateTime? lastPing = r.LastPingAt;

            if (r.TripId is null || r.LastPingAt is null)
            {
                status = "idle";
            }
            else if (!GpsAllowed)
            {
                status = "idle";
                lat = null;
                lng = null;
                speed = null;
                lastPing = null;
            }
            else
            {
                var ageMin = (now - r.LastPingAt.Value).TotalMinutes;
                status = ageMin > 5 ? "delayed"
                    : (r.SpeedKmh is <= 3) ? "at_stop"
                    : "on_route";
                nextStop = (await busRepo.GetPositionAsync(r.BusId, ct)).NextStopName;
            }
            list.Add(new ChildBusPositionResponse(
                r.StudentId, r.StudentName, r.AdmissionNo, r.BusId, r.BusNo, r.RouteName, status,
                lat, lng, speed, nextStop, lastPing));
        }
        return ApiResult<IReadOnlyList<ChildBusPositionResponse>>.Ok(list);
    }
}
