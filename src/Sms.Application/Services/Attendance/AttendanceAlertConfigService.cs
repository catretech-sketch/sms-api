using System.Text.RegularExpressions;
using Sms.Application.Common;
using Sms.Modules.Attendance;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Attendance;

public interface IAttendanceAlertConfigService
{
    Task<ApiResult<AttendanceAlertConfigResponse>> GetAsync(CancellationToken ct = default);
    Task<ApiResult<AttendanceAlertConfigResponse>> UpsertAsync(
        UpsertAttendanceAlertConfigRequest req, CancellationToken ct = default);
}

/// School-level absence-alert config. Thresholds are clamped and the schedule
/// normalised so the browser and any server-side sender agree on one source of truth.
public sealed partial class AttendanceAlertConfigService(
    AttendanceAlertConfigRepository repo, ITenantContext tenant) : IAttendanceAlertConfigService
{
    private const int MinDays = 1;
    private const int MaxDays = 60;
    private static readonly AttendanceAlertConfigResponse Default =
        new(3, 5, false, "09:00", "app", null);

    public async Task<ApiResult<AttendanceAlertConfigResponse>> GetAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<AttendanceAlertConfigResponse>.Fail(new Error("forbidden", "no tenant context"), 403);
        var cfg = await repo.GetAsync(tid, ct) ?? Default;
        return ApiResult<AttendanceAlertConfigResponse>.Ok(Normalize(cfg));
    }

    public async Task<ApiResult<AttendanceAlertConfigResponse>> UpsertAsync(
        UpsertAttendanceAlertConfigRequest req, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tid)
            return ApiResult<AttendanceAlertConfigResponse>.Fail(new Error("forbidden", "no tenant context"), 403);

        var notice = Clamp(req.NoticeDays, Default.NoticeDays);
        var email = Math.Max(notice, Clamp(req.EmailDays, Default.EmailDays));
        var clean = new UpsertAttendanceAlertConfigRequest(
            notice, email, req.AutoSend, NormTime(req.AutoTime), NormChannel(req.AutoChannel));

        var saved = await repo.UpsertAsync(tid, clean, ct);
        var result = saved is null
            ? new AttendanceAlertConfigResponse(notice, email, clean.AutoSend, clean.AutoTime!, clean.AutoChannel!, null)
            : Normalize(saved);
        return ApiResult<AttendanceAlertConfigResponse>.Ok(result);
    }

    private static AttendanceAlertConfigResponse Normalize(AttendanceAlertConfigResponse c)
    {
        var notice = Clamp(c.NoticeDays, Default.NoticeDays);
        var email = Math.Max(notice, Clamp(c.EmailDays, Default.EmailDays));
        return c with
        {
            NoticeDays = notice,
            EmailDays = email,
            AutoTime = NormTime(c.AutoTime)!,
            AutoChannel = NormChannel(c.AutoChannel)!,
        };
    }

    private static int Clamp(int v, int fallback) =>
        v < MinDays ? Math.Max(MinDays, fallback) : v > MaxDays ? MaxDays : v;

    private static string NormTime(string? v) =>
        v is not null && TimeRegex().IsMatch(v) ? v : Default.AutoTime;

    private static string NormChannel(string? v) =>
        string.Equals(v, "email", StringComparison.OrdinalIgnoreCase) ? "email" : "app";

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimeRegex();
}
