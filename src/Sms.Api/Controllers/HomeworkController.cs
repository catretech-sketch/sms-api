using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class HomeworkController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("homework")]
    public async Task<IActionResult> List(
        [FromQuery(Name = "student_id")] Guid? studentId, [FromQuery] string? status, CancellationToken ct) =>
        FromResult(await academics.ListHomeworkAsync(studentId, status, ct));

    [HttpGet("homework/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetHomeworkAsync(id, ct));

    [HttpPost("homework")]
    public async Task<IActionResult> Create([FromBody] CreateHomeworkRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateHomeworkAsync(req, ct));

    [HttpPatch("homework/{id:guid}")]
    public async Task<IActionResult> SetStatus(
        Guid id, [FromBody] SetHomeworkStatusRequest req, CancellationToken ct) =>
        FromResult(await academics.SetHomeworkStatusAsync(id, req, ct));

    [HttpPost("homework/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) =>
        FromResult(await academics.SubmitHomeworkAsync(id, ct));
}
