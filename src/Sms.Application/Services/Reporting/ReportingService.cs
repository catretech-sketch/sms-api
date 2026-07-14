using Sms.Application.Common;
using Sms.Modules.Reporting.Contracts;
using Sms.Modules.Reporting.Data;
using Sms.Shared.Kernel.Time;

namespace Sms.Application.Services.Reporting;

public interface IReportingService
{
    Task<ApiResult<DashboardStatsResponse>> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<ApiResult<PrincipalOverviewResponse>> GetPrincipalOverviewAsync(CancellationToken ct = default);
    Task<ApiResult<PrincipalAttendanceResponse>> GetPrincipalAttendanceAsync(CancellationToken ct = default);
}

public sealed class ReportingService(ReportingRepository repo, IClock clock) : IReportingService
{
    public async Task<ApiResult<DashboardStatsResponse>> GetDashboardStatsAsync(CancellationToken ct = default) =>
        ApiResult<DashboardStatsResponse>.Ok(await repo.GetDashboardStatsAsync(clock.UtcNow, ct));

    public async Task<ApiResult<PrincipalOverviewResponse>> GetPrincipalOverviewAsync(CancellationToken ct = default) =>
        ApiResult<PrincipalOverviewResponse>.Ok(await repo.GetPrincipalOverviewAsync(clock.UtcNow, ct));

    public async Task<ApiResult<PrincipalAttendanceResponse>> GetPrincipalAttendanceAsync(CancellationToken ct = default) =>
        ApiResult<PrincipalAttendanceResponse>.Ok(await repo.GetPrincipalAttendanceAsync(clock.UtcNow, ct));
}
