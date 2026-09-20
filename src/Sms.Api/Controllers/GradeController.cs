using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Application.Services.Sis;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class GradeController(IAcademicsService academics, ISisService sis) : ApiControllerBase
{
    [HttpGet("exam-papers/{examPaperId:guid}/grades")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> List(Guid examPaperId, CancellationToken ct) =>
        FromResult(await academics.ListGradesAsync(examPaperId, ct));

    /// <summary>All marks for one roster student (one round-trip for the student app report card).</summary>
    [HttpGet("grades")]
    public async Task<IActionResult> ListForStudent([FromQuery] Guid student_id, CancellationToken ct)
    {
        if (student_id == Guid.Empty)
            return BadRequestResult("student_id is required");
        if (!RoleChecks.IsStaff(User) && !await sis.IsLinkedToCallerAsync(student_id, ct))
            return ForbiddenResult("not your linked student");
        return FromResult(await academics.ListGradesForStudentAsync(student_id, ct));
    }

    [HttpPut("grades")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> Upsert([FromBody] UpsertGradeRequest req, CancellationToken ct) =>
        FromResult(await academics.UpsertGradeAsync(req, ct));
}
