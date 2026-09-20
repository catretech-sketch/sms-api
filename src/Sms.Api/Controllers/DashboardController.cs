using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class DashboardController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("dashboard/overview")]
    public async Task<IActionResult> Overview(CancellationToken ct) =>
        OkData(await tenancy.GetDashboardOverviewAsync(ct));
}
