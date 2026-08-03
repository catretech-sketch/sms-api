using System.Text.RegularExpressions;
using Sms.Application.Common;
using Sms.Modules.Attendance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Attendance;

public interface IAttendanceService
{
    Task<ApiResult<SchoolLocationResponse>> GetSchoolLocationAsync(CancellationToken ct = default);
    Task<ApiResult<SchoolLocationResponse>> UpsertSchoolLocationAsync(UpsertSchoolLocationRequest req, CancellationToken ct = default);
    Task<ApiResult> DeleteSchoolLocationAsync(CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceDayResponse>> PunchAsync(PunchRequest req, CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceDayResponse>> GetTodayAsync(string? date, int? offsetMinutes, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>> GetHistoryAsync(int? limit, int? offsetMinutes, CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceSummaryResponse>> GetSummaryAsync(string? month, CancellationToken ct = default);
}

public sealed class AttendanceService(
    CheckInRepository repo, ITenantContext tenant, ITenantFeatureSet features, IClock clock) : IAttendanceService
{
    private bool StaffCheckInAllowed => tenant.IsPlatform || features.Has(FeatureCatalog.Attendance);
    private bool GeofenceAllowed => tenant.IsPlatform || features.Has(FeatureCatalog.AttendanceGeofence);

    private static ApiResult<T> GeofenceLocked<T>() =>
        ApiResult<T>.Fail(new Error("feature_locked",
            $"This feature ({FeatureCatalog.AttendanceGeofence}) is not available on your plan."), 403);

    private static ApiResult<T> StaffCheckInLocked<T>() =>
        ApiResult<T>.Fail(new Error("feature_locked",
            $"This feature ({FeatureCatalog.Attendance}) is not available on your plan."), 403);

    public async Task<ApiResult<SchoolLocationResponse>> GetSchoolLocationAsync(CancellationToken ct = default)
    {
        if (!GeofenceAllowed) return GeofenceLocked<SchoolLocationResponse>();
        if (tenant.TenantId is not { } tid)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var loc = await repo.GetSchoolLocationAsync(tid, ct);
        if (loc is null)
            return ApiResult<SchoolLocationResponse>.Fail(
                new Error("school_location_not_configured", "School location is not configured. Ask your admin to set campus coordinates."), 404);
        return ApiResult<SchoolLocationResponse>.Ok(loc);
    }

    public async Task<ApiResult<SchoolLocationResponse>> UpsertSchoolLocationAsync(UpsertSchoolLocationRequest req, CancellationToken ct = default)
    {
        if (!GeofenceAllowed) return GeofenceLocked<SchoolLocationResponse>();
        if (tenant.TenantId is not { } tid)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        if (req.Lat is < -90 or > 90 || req.Lng is < -180 or > 180)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("invalid_coordinates", "lat/lng out of range"), 422);
        if (req.RadiusMeters is < 30 or > 5000)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("invalid_radius", "radius_meters must be 30–5000"), 422);
        if (req.Lat == 0 && req.Lng == 0)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("invalid_coordinates", "campus coordinates cannot be 0,0"), 422);
        return ApiResult<SchoolLocationResponse>.Ok((await repo.UpsertSchoolLocationAsync(tid, req, ct))!);
    }

    public async Task<ApiResult> DeleteSchoolLocationAsync(CancellationToken ct = default)
    {
        if (!GeofenceAllowed)
            return ApiResult.Fail(new Error("feature_locked",
                $"This feature ({FeatureCatalog.AttendanceGeofence}) is not available on your plan."), 403);
        if (tenant.TenantId is not { } tid)
            return ApiResult.Fail(new Error("forbidden", "no tenant context"), 403);
        await repo.DeleteSchoolLocationAsync(tid, ct);
        return ApiResult.Ok();
    }

    public async Task<ApiResult<TeacherAttendanceDayResponse>> PunchAsync(PunchRequest req, CancellationToken ct = default)
    {
        if (!StaffCheckInAllowed) return StaffCheckInLocked<TeacherAttendanceDayResponse>();
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TeacherAttendanceDayResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        var atUtc = req.At.Kind switch
        {
            DateTimeKind.Utc => req.At,
            DateTimeKind.Local => req.At.ToUniversalTime(),
            _ => DateTime.SpecifyKind(req.At, DateTimeKind.Utc),
        };
        var normalized = req with { At = atUtc };
        try
        {
            if (GeofenceAllowed)
            {
                var loc = await repo.GetSchoolLocationAsync(tid, ct);
                if (loc is null)
                    await repo.ManualPunchAsync(tid, uid, normalized.Kind, atUtc, ct);
                else
                    await repo.PunchAsync(tid, uid, normalized, ct);
            }
            else
                await repo.ManualPunchAsync(tid, uid, normalized.Kind, atUtc, ct);
        }
        catch (GeofencePunchRejectedException ex)
        {
            return ApiResult<TeacherAttendanceDayResponse>.Fail(new Error(ex.ErrorCode, ex.Message), ex.StatusCode);
        }
        var offset = ParseUtcOffset(req.OffsetMinutes);
        var day = DateOnly.FromDateTime(atUtc.Add(offset));
        var dayResult = await repo.GetTodayAsync(uid, day, offset, ct);
        return ApiResult<TeacherAttendanceDayResponse>.Ok(dayResult, 201);
    }

    public async Task<ApiResult<TeacherAttendanceDayResponse>> GetTodayAsync(
        string? date, int? offsetMinutes, CancellationToken ct = default)
    {
        if (!StaffCheckInAllowed) return StaffCheckInLocked<TeacherAttendanceDayResponse>();
        if (tenant.UserId is not { } uid)
            return ApiResult<TeacherAttendanceDayResponse>.Fail(new Error("forbidden", "no user context"), 403);

        var offset = ParseUtcOffset(offsetMinutes);
        var day = ParseLocalDate(date) ?? DateOnly.FromDateTime(clock.UtcNow.Add(offset));
        return ApiResult<TeacherAttendanceDayResponse>.Ok(await repo.GetTodayAsync(uid, day, offset, ct));
    }

    private static DateOnly? ParseLocalDate(string? date)
    {
        if (date is null) return null;
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", out var d) ? d : null;
    }

    private static TimeSpan ParseUtcOffset(int? offsetMinutes)
    {
        if (offsetMinutes is null) return TimeSpan.Zero;
        var clamped = Math.Clamp(offsetMinutes.Value, -14 * 60, 14 * 60);
        return TimeSpan.FromMinutes(clamped);
    }

    public async Task<ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>> GetHistoryAsync(
        int? limit, int? offsetMinutes, CancellationToken ct = default)
    {
        if (!StaffCheckInAllowed) return StaffCheckInLocked<IReadOnlyList<TeacherAttendanceDayResponse>>();
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>.Fail(new Error("forbidden", "no user context"), 403);
        var take = limit is > 0 and <= 366 ? limit.Value : 30;
        var offset = ParseUtcOffset(offsetMinutes);
        return ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>.Ok(
            await repo.GetHistoryAsync(uid, take, offset, ct));
    }

    public async Task<ApiResult<TeacherAttendanceSummaryResponse>> GetSummaryAsync(string? month, CancellationToken ct = default)
    {
        if (!StaffCheckInAllowed) return StaffCheckInLocked<TeacherAttendanceSummaryResponse>();
        if (tenant.UserId is not { } uid)
            return ApiResult<TeacherAttendanceSummaryResponse>.Fail(new Error("forbidden", "no user context"), 403);
        var now = clock.UtcNow;
        int year = now.Year, m = now.Month;
        if (month is not null)
        {
            if (!Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
                return ApiResult<TeacherAttendanceSummaryResponse>.Fail(new Error("invalid_month", "month must be YYYY-MM"), 422);
            year = int.Parse(month[..4]);
            m = int.Parse(month[5..]);
        }
        return ApiResult<TeacherAttendanceSummaryResponse>.Ok(await repo.GetSummaryAsync(uid, year, m, ct));
    }
}
