using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class GradeController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("exam-papers/{examPaperId:guid}/grades")]
    public async Task<IActionResult> List(Guid examPaperId, CancellationToken ct) =>
        FromResult(await academics.ListGradesAsync(examPaperId, ct));

    [HttpPut("grades")]
    public async Task<IActionResult> Upsert([FromBody] UpsertGradeRequest req, CancellationToken ct) =>
        FromResult(await academics.UpsertGradeAsync(req, ct));
}
