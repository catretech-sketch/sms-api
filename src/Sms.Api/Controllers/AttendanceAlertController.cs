using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Attendance;
using Sms.Modules.Attendance;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/attendance")]
[Authorize]
public sealed class AttendanceAlertController(IAttendanceAlertConfigService config) : ApiControllerBase
{
    [HttpGet("alert-config")]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        FromResult(await config.GetAsync(ct));

    [HttpPut("alert-config")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Upsert([FromBody] UpsertAttendanceAlertConfigRequest req, CancellationToken ct) =>
        FromResult(await config.UpsertAsync(req, ct));
}
