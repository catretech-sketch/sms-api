using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Dashboard;

namespace Sms.Api.Controllers;

[Route("v1/staff")]
[Authorize]
public sealed class StaffDashboardController(IDashboardService dashboard) : ApiControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Get([FromQuery] int? offset_minutes, CancellationToken ct) =>
        FromResult(await dashboard.GetAsync(offset_minutes, ct));
}
