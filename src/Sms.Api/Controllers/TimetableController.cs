using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class TimetableController(IAcademicsService academics, ISisService sis) : ApiControllerBase
{
    [HttpGet("timetable")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (IsStaff(User))
            return FromResult(await academics.ListTimetableAsync(User, ct));

        var me = await sis.GetMyStudentAsync(ct);
        if (me.Error is not null)
            return FromResult(me);
        return FromResult(await academics.ListTimetableForStudentAsync(
            me.Data!.Grade, me.Data.Section, me.Data.ClassLabel, ct));
    }

    [HttpPost("timetable")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Create([FromBody] CreateTimetableSlotRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateTimetableSlotAsync(req, ct));

    /// One-shot publish: clear slots for class_ids, then insert slots. Avoids N× POST/DELETE.
    [HttpPut("timetable/replace")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Replace([FromBody] ReplaceTimetableRequest req, CancellationToken ct) =>
        FromResult(await academics.ReplaceTimetableAsync(req, ct));

    [HttpDelete("timetable/{id:guid}")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await academics.DeleteTimetableSlotAsync(id, ct));

    private static bool IsStaff(ClaimsPrincipal user)
    {
        foreach (var claim in user.FindAll("role"))
        {
            var role = claim.Value.ToLowerInvariant();
            if (role.Contains("admin") || role.Contains("teacher") || role.Contains("principal")
                || role.Contains("owner") || role is "staff" || role.Contains("platform"))
                return true;
        }
        return false;
    }
}
