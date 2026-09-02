using Sms.Application.Common;
using Sms.Application.Services.Attendance;
using Sms.Modules.Staffing.Contracts;
using Sms.Modules.Staffing.Data;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Results;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Application.Services.Dashboard;

public interface IDashboardService
{
    Task<ApiResult<DashboardResponse>> GetAsync(int? offsetMinutes, CancellationToken ct = default);
}

/// Composes the staff dashboard entirely from existing, already-correct data sources — no new
/// table or proc. hoursThisWeek reuses the same CheckIns-derived hours logic as the teacher
/// attendance summary; roleCard reuses the driver/conductor bus assignment lookups from
/// Transport. Every field with no real backing data (leaveLeft, streakDays, hoursTarget,
/// pendingTasksPeek, alert, and non-driver/conductor role cards) is simply omitted, never
/// fabricated — see the 2026-09-02 dashboard design decision.
public sealed class DashboardService(
    StaffRepository staff, TripRepository trips, IAttendanceService attendance, ITenantContext tenant) : IDashboardService
{
    public async Task<ApiResult<DashboardResponse>> GetAsync(int? offsetMinutes, CancellationToken ct = default)
    {
        if (tenant.UserId is not { } uid)
            return ApiResult<DashboardResponse>.Fail(new Error("forbidden", "no user context"), 403);

        var hoursResult = await attendance.GetWeekSummaryAsync(offsetMinutes, ct);
        if (hoursResult.Error is { } error)
            return ApiResult<DashboardResponse>.Fail(error, hoursResult.StatusCode);

        var roleCard = await ResolveRoleCardAsync(uid, ct);
        return ApiResult<DashboardResponse>.Ok(new DashboardResponse(hoursResult.Data, roleCard));
    }

    private async Task<RoleCardResponse?> ResolveRoleCardAsync(Guid userId, CancellationToken ct)
    {
        var category = (await staff.GetCategoryByUserIdAsync(userId, ct))?.Trim().ToLowerInvariant();
        switch (category)
        {
            case "driver":
                var driverBus = await trips.GetDriverBusRouteAsync(userId, ct);
                return driverBus is null ? null : new RoleCardResponse("driver", driverBus.BusNo, driverBus.RouteName);
            case "conductor":
                var conductorBus = await trips.GetConductorBusRouteAsync(userId, ct);
                return conductorBus is null ? null : new RoleCardResponse("conductor", conductorBus.BusNo, conductorBus.RouteName);
            default:
                return null;
        }
    }
}
