using Sms.Application.Common;
using Sms.Modules.Attendance;
using Sms.Shared.Kernel.Results;

namespace Sms.Application.Services.Attendance;

public interface IStaffAttendanceService
{
    Task<ApiResult<StaffAttendanceResponse>> GetTodayAsync(int? offsetMinutes, CancellationToken ct = default);
    Task<ApiResult<StaffAttendanceResponse>> CheckInAsync(StaffCheckRequest req, CancellationToken ct = default);
    Task<ApiResult<StaffAttendanceResponse>> CheckOutAsync(StaffCheckRequest req, CancellationToken ct = default);
}

/// Adapter over the existing teacher-app attendance service for the sms-staff app's
/// check_in/check_out/last_log shape — no new table, proc, or geofence logic of its own.
/// Both check-in and check-out are server-verified from lat/lng/accuracy via the same
/// PunchAsync geofence check teacher check-ins already use, never a client-supplied "in zone"
/// flag (see the 2026-09-02 attendance design decision).
public sealed class StaffAttendanceService(IAttendanceService attendance) : IStaffAttendanceService
{
    public async Task<ApiResult<StaffAttendanceResponse>> GetTodayAsync(int? offsetMinutes, CancellationToken ct = default)
    {
        var dayResult = await attendance.GetTodayAsync(null, offsetMinutes, ct);
        if (dayResult.Error is { } error)
            return ApiResult<StaffAttendanceResponse>.Fail(error, dayResult.StatusCode);
        return ApiResult<StaffAttendanceResponse>.Ok(await ComposeAsync(dayResult.Data!, ct));
    }

    public async Task<ApiResult<StaffAttendanceResponse>> CheckInAsync(StaffCheckRequest req, CancellationToken ct = default) =>
        await PunchAsync("in", req, ct);

    public async Task<ApiResult<StaffAttendanceResponse>> CheckOutAsync(StaffCheckRequest req, CancellationToken ct = default) =>
        await PunchAsync("out", req, ct);

    private async Task<ApiResult<StaffAttendanceResponse>> PunchAsync(string kind, StaffCheckRequest req, CancellationToken ct)
    {
        var punchResult = await attendance.PunchAsync(
            new PunchRequest(kind, req.At, req.Lat, req.Lng, req.AccuracyMeters, req.OffsetMinutes), ct);
        if (punchResult.Error is { } error)
            return ApiResult<StaffAttendanceResponse>.Fail(error, punchResult.StatusCode);
        return ApiResult<StaffAttendanceResponse>.Ok(await ComposeAsync(punchResult.Data!, ct), 201);
    }

    private async Task<StaffAttendanceResponse> ComposeAsync(TeacherAttendanceDayResponse day, CancellationToken ct)
    {
        var locationResult = await attendance.GetSchoolLocationAsync(ct);
        var location = locationResult.Data;

        var log = new List<StaffAttendanceLogEntry>();
        if (day.CheckIn is { } checkIn) log.Add(new StaffAttendanceLogEntry(checkIn.Kind, checkIn.At, checkIn.Verified));
        if (day.CheckOut is { } checkOut) log.Add(new StaffAttendanceLogEntry(checkOut.Kind, checkOut.At, checkOut.Verified));

        return new StaffAttendanceResponse(
            CheckedIn: day.CheckIn is not null && day.CheckOut is null,
            CheckInAt: day.CheckIn?.At,
            LastLog: log,
            DutyPost: location?.Name ?? "",
            GeofenceRadiusM: location?.RadiusMeters ?? 0);
    }
}
