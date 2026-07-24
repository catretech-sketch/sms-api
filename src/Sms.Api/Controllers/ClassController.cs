using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class ClassController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("classes")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListClassesAsync(ct));

    [HttpGet("classes/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetClassAsync(id, ct));

    [HttpPost("classes")]
    public async Task<IActionResult> Create([FromBody] CreateClassRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateClassAsync(req, ct));

    [HttpPatch("classes/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateClassRequest req, CancellationToken ct) =>
        FromResult(await academics.UpdateClassAsync(id, req, ct));

    [HttpGet("classes/{classId:guid}/attendance")]
    public async Task<IActionResult> ListAttendance(Guid classId, [FromQuery] DateTime date, CancellationToken ct) =>
        FromResult(await academics.ListAttendanceAsync(classId, date, ct));

    [HttpPost("classes/{classId:guid}/attendance")]
    public async Task<IActionResult> BulkUpsertAttendance(
        Guid classId, [FromBody] BulkAttendanceRequest req, CancellationToken ct) =>
        FromResult(await academics.BulkUpsertAttendanceAsync(classId, req, ct));
}
