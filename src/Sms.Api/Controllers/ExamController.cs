using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class ExamController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("exams")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListExamsAsync(ct));

    [HttpGet("exams/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetExamAsync(id, ct));

    [HttpGet("exams/{id:guid}/letter-grades")]
    public async Task<IActionResult> LetterGrades(Guid id, CancellationToken ct) =>
        FromResult(await academics.CountLetterGradesForExamAsync(id, ct));

    [HttpPost("exams")]
    [Authorize(Policy = Sms.Shared.Kernel.Authz.Policies.SchoolAdmin)]
    public async Task<IActionResult> Create([FromBody] CreateExamRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateExamAsync(req, ct));

    [HttpPatch("exams/{id:guid}")]
    [Authorize(Policy = Sms.Shared.Kernel.Authz.Policies.SchoolAdmin)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExamRequest req, CancellationToken ct) =>
        FromResult(await academics.UpdateExamAsync(id, req, ct));
}
