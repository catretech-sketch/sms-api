using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Modules.Sis.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class SubjectController(IAcademicsService academics, ISisService sis) : ApiControllerBase
{
    [HttpGet("subjects")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        if (IsStaff(User) && studentId is null)
            return FromResult(await academics.ListSubjectsAsync(ct));

        StudentResponse roster;
        if (studentId is Guid sid)
        {
            var row = await sis.GetStudentAsync(sid, ct);
            if (row.Error is not null)
                return FromResult(row);
            roster = row.Data!;
        }
        else
        {
            var me = await sis.GetMyStudentAsync(ct);
            if (me.Error is not null)
                return FromResult(me);
            roster = me.Data!;
        }

        return FromResult(await academics.ListSubjectsForStudentAsync(
            roster.Grade, roster.Section, roster.ClassLabel, ct));
    }

    [HttpGet("subjects/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetSubjectAsync(id, ct));

    [HttpPost("subjects")]
    public async Task<IActionResult> Create([FromBody] CreateSubjectRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateSubjectAsync(req, ct));

    [HttpPatch("subjects/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSubjectRequest req, CancellationToken ct) =>
        FromResult(await academics.UpdateSubjectAsync(id, req, ct));

    [HttpDelete("subjects/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await academics.DeleteSubjectAsync(id, ct));

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
