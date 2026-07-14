using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Staffing;
using Sms.Modules.Staffing.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class TeacherController(IStaffingService staffing) : ApiControllerBase
{
    [HttpGet("teachers")]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] string? dept, [FromQuery] string? status, CancellationToken ct) =>
        FromCursorResult(await staffing.ListTeachersAsync(q, dept, status, ct));

    [HttpGet("teachers/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await staffing.GetTeacherAsync(id, ct));

    [HttpPost("teachers")]
    public async Task<IActionResult> Create([FromBody] CreateTeacherRequest req, CancellationToken ct) =>
        FromResult(await staffing.CreateTeacherAsync(req, ct));

    [HttpPatch("teachers/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeacherRequest req, CancellationToken ct) =>
        FromResult(await staffing.UpdateTeacherAsync(id, req, ct));
}
