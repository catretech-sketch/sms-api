using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Sis;
using Sms.Modules.Sis.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class StudentController(ISisService sis) : ApiControllerBase
{
    [HttpGet("students")]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? grade, [FromQuery] string? status, [FromQuery] string? fee,
        CancellationToken ct) =>
        FromCursorResult(await sis.ListStudentsAsync(q, grade, status, fee, ct));

    [HttpGet("students/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await sis.GetStudentAsync(id, ct));

    [HttpPost("students")]
    public async Task<IActionResult> Create([FromBody] CreateStudentRequest req, CancellationToken ct) =>
        FromResult(await sis.CreateStudentAsync(req, ct));

    [HttpPatch("students/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudentRequest req, CancellationToken ct) =>
        FromResult(await sis.UpdateStudentAsync(id, req, ct));

    [HttpGet("classes/{classId:guid}/students")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> ListByClass(
        Guid classId, [FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct) =>
        FromCursorResult(await sis.ListClassStudentsAsync(classId, limit, cursor, ct));
}
