using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Reporting;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class ReportingController(IReportingService reporting) : ApiControllerBase
{
    [HttpGet("dashboard/stats")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> GetDashboardStats(CancellationToken ct) =>
        FromResult(await reporting.GetDashboardStatsAsync(ct));

    [HttpGet("principal/overview")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> GetPrincipalOverview(CancellationToken ct) =>
        FromResult(await reporting.GetPrincipalOverviewAsync(ct));

    [HttpGet("principal/attendance")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> GetPrincipalAttendance(CancellationToken ct) =>
        FromResult(await reporting.GetPrincipalAttendanceAsync(ct));
}
