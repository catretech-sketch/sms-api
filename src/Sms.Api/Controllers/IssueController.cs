using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Issues;
using Sms.Modules.Issues;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class IssueController(IIssueService issues) : ApiControllerBase
{
    [HttpPost("staff/issues")]
    public async Task<IActionResult> Create([FromBody] CreateIssueRequest req, CancellationToken ct) =>
        FromResult(await issues.CreateAsync(req, ct));

    [HttpGet("staff/issues")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        FromResult(await issues.ListAsync(status, User, ct));

    [HttpGet("staff/issues/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await issues.GetAsync(id, User, ct));

    [HttpPatch("issues/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIssueRequest req, CancellationToken ct) =>
        FromResult(await issues.UpdateAsync(id, req, User, ct));
}
