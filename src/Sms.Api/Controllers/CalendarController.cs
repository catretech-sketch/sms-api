using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class CalendarController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("calendar")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListCalendarEventsAsync(ct));

    [HttpPost("calendar")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarEventRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateCalendarEventAsync(req, ct));
}
