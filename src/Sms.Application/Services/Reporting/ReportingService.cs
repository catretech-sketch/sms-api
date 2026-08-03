using Sms.Application.Common;
using Sms.Modules.Reporting.Contracts;
using Sms.Modules.Reporting.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Reporting;

public interface IReportingService
{
    Task<ApiResult<DashboardStatsResponse>> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<ApiResult<PrincipalOverviewResponse>> GetPrincipalOverviewAsync(int? offsetMinutes = null, CancellationToken ct = default);
    Task<ApiResult<PrincipalAttendanceResponse>> GetPrincipalAttendanceAsync(
        DateTime? date = null, int? offsetMinutes = null, CancellationToken ct = default);
}

public sealed class ReportingService(ReportingRepository repo, ITenantFeatureSet features, IClock clock) : IReportingService
{
    private bool GeofenceAllowed => features.Has(FeatureCatalog.AttendanceGeofence);

    /// Silver/Gold plans allow manual staff check-in; hide GPS verification only.
    private static PrincipalStaffEntry StripGeoVerification(PrincipalStaffEntry s) =>
        s with { CheckInVerified = false };

    private static TimeSpan ParseUtcOffset(int? offsetMinutes)
    {
        if (offsetMinutes is null) return TimeSpan.Zero;
        return TimeSpan.FromMinutes(Math.Clamp(offsetMinutes.Value, -14 * 60, 14 * 60));
    }

    private DateOnly ResolveLocalDay(DateTime? date, TimeSpan utcOffset)
    {
        if (date is { } d) return DateOnly.FromDateTime(d.Date);
        return DateOnly.FromDateTime(clock.UtcNow.Add(utcOffset));
    }

    public async Task<ApiResult<DashboardStatsResponse>> GetDashboardStatsAsync(CancellationToken ct = default) =>
        ApiResult<DashboardStatsResponse>.Ok(await repo.GetDashboardStatsAsync(clock.UtcNow, ct));

    public async Task<ApiResult<PrincipalOverviewResponse>> GetPrincipalOverviewAsync(
        int? offsetMinutes = null, CancellationToken ct = default)
    {
        var offset = ParseUtcOffset(offsetMinutes);
        var day = DateOnly.FromDateTime(clock.UtcNow.Add(offset));
        var raw = await repo.GetPrincipalOverviewAsync(day, offset, ct);
        if (GeofenceAllowed) return ApiResult<PrincipalOverviewResponse>.Ok(raw);
        var staff = raw.Staff.Select(StripGeoVerification).ToList();
        return ApiResult<PrincipalOverviewResponse>.Ok(new PrincipalOverviewResponse(raw.Kpis, staff));
    }

    public async Task<ApiResult<PrincipalAttendanceResponse>> GetPrincipalAttendanceAsync(
        DateTime? date = null, int? offsetMinutes = null, CancellationToken ct = default)
    {
        var offset = ParseUtcOffset(offsetMinutes);
        var day = ResolveLocalDay(date, offset);
        var raw = await repo.GetPrincipalAttendanceAsync(day, offset, ct);
        if (GeofenceAllowed) return ApiResult<PrincipalAttendanceResponse>.Ok(raw);
        var staff = raw.Staff.Select(StripGeoVerification).ToList();
        return ApiResult<PrincipalAttendanceResponse>.Ok(raw with { Staff = staff });
    }
}
