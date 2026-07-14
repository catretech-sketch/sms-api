using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Comms;
using Sms.Modules.Comms;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class AnnouncementController(IAnnouncementService announcements) : ApiControllerBase
{
    [HttpGet("announcements")]
    public async Task<IActionResult> List([FromQuery] string? audience, CancellationToken ct) =>
        FromResult(await announcements.ListAsync(audience, ct));

    [HttpPost("announcements")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest req, CancellationToken ct)
    {
        var role = User.FindFirst("role")?.Value;
        return FromResult(await announcements.CreateAsync(req, role, ct));
    }
}
