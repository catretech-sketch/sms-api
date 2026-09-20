using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Modules.Academics.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class AssignmentController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("assignments")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListAssignmentsAsync(ct));

    [HttpPost("assignments")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> Create([FromBody] CreateAssignmentRequest req, CancellationToken ct) =>
        FromResult(await academics.CreateAssignmentAsync(req, ct));

    [HttpPatch("assignments/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TeacherApp)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] CreateAssignmentRequest req, CancellationToken ct) =>
        FromResult(await academics.UpdateAssignmentAsync(id, req, ct));
}
