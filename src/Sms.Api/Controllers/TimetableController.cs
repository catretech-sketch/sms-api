using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class TimetableController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("timetable")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListTimetableAsync(ct));

    [HttpPost("timetable")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Create([FromBody] CreateTimetableSlotRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateTimetableSlotAsync(req, ct));
}
