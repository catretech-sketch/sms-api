using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class SubjectController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("subjects")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListSubjectsAsync(ct));

    [HttpGet("subjects/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetSubjectAsync(id, ct));

    [HttpPost("subjects")]
    public async Task<IActionResult> Create([FromBody] CreateSubjectRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateSubjectAsync(req, ct));
}
