using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class PersonExtrasController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("students/{id:guid}/extras")]
    public async Task<IActionResult> GetStudent(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetPersonExtrasAsync("student", id, ct));

    [HttpPut("students/{id:guid}/extras")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> PutStudent(Guid id, [FromBody] UpsertPersonExtrasRequest req, CancellationToken ct) =>
        FromResult(await academics.UpsertPersonExtrasAsync("student", id, req, ct));

    [HttpGet("teachers/{id:guid}/extras")]
    public async Task<IActionResult> GetTeacher(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetPersonExtrasAsync("teacher", id, ct));

    [HttpPut("teachers/{id:guid}/extras")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> PutTeacher(Guid id, [FromBody] UpsertPersonExtrasRequest req, CancellationToken ct) =>
        FromResult(await academics.UpsertPersonExtrasAsync("teacher", id, req, ct));

    [HttpGet("staff/{id:guid}/extras")]
    public async Task<IActionResult> GetStaff(Guid id, CancellationToken ct) =>
        FromResult(await academics.GetPersonExtrasAsync("staff", id, ct));

    [HttpPut("staff/{id:guid}/extras")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> PutStaff(Guid id, [FromBody] UpsertPersonExtrasRequest req, CancellationToken ct) =>
        FromResult(await academics.UpsertPersonExtrasAsync("staff", id, req, ct));
}
