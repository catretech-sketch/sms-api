using System.Text.RegularExpressions;
using Sms.Application.Common;
using Sms.Modules.Attendance;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Attendance;

public interface IAttendanceService
{
    Task<ApiResult<SchoolLocationResponse>> GetSchoolLocationAsync(CancellationToken ct = default);
    Task<ApiResult<SchoolLocationResponse>> UpsertSchoolLocationAsync(UpsertSchoolLocationRequest req, CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceDayResponse>> PunchAsync(PunchRequest req, CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceDayResponse>> GetTodayAsync(CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>> GetHistoryAsync(int? limit, CancellationToken ct = default);
    Task<ApiResult<TeacherAttendanceSummaryResponse>> GetSummaryAsync(string? month, CancellationToken ct = default);
}

public sealed class AttendanceService(CheckInRepository repo, ITenantContext tenant, IClock clock) : IAttendanceService
{
    public async Task<ApiResult<SchoolLocationResponse>> GetSchoolLocationAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var loc = await repo.GetSchoolLocationAsync(tid, ct) ?? new SchoolLocationResponse(0, 0, 50, null);
        return ApiResult<SchoolLocationResponse>.Ok(loc);
    }

    public async Task<ApiResult<SchoolLocationResponse>> UpsertSchoolLocationAsync(UpsertSchoolLocationRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<SchoolLocationResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        return ApiResult<SchoolLocationResponse>.Ok((await repo.UpsertSchoolLocationAsync(tid, req, ct))!);
    }

    public async Task<ApiResult<TeacherAttendanceDayResponse>> PunchAsync(PunchRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid || tenant.UserId is not { } uid)
            return ApiResult<TeacherAttendanceDayResponse>.Fail(new Error("forbidden", "no tenant/user context"), 403);
        await repo.PunchAsync(tid, uid, req, ct);
        var day = await repo.GetTodayAsync(uid, req.At, ct);
        return ApiResult<TeacherAttendanceDayResponse>.Ok(day, 201);
    }

    public async Task<ApiResult<TeacherAttendanceDayResponse>> GetTodayAsync(CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<TeacherAttendanceDayResponse>.Fail(new Error("forbidden", "no user context"), 403);
        return ApiResult<TeacherAttendanceDayResponse>.Ok(await repo.GetTodayAsync(uid, DateTime.UtcNow, ct));
    }

    public async Task<ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>> GetHistoryAsync(int? limit, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>.Fail(new Error("forbidden", "no user context"), 403);
        var take = limit is > 0 and <= 366 ? limit.Value : 30;
        return ApiResult<IReadOnlyList<TeacherAttendanceDayResponse>>.Ok(await repo.GetHistoryAsync(uid, take, ct));
    }

    public async Task<ApiResult<TeacherAttendanceSummaryResponse>> GetSummaryAsync(string? month, CancellationToken ct = default)
    {
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
