using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class ComplaintController(IComplaintService complaints) : ApiControllerBase
{
    [HttpGet("complaints")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        FromResult(await complaints.ListAsync(status, User, ct));

    [HttpPost("complaints")]
    public async Task<IActionResult> Create([FromBody] CreateComplaintRequest req, CancellationToken ct) =>
        FromResult(await complaints.CreateAsync(req, ct));

    [HttpPatch("complaints/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateComplaintRequest req, CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await complaints.UpdateAsync(id, req, ct));
    }
}
