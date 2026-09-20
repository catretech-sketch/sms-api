using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Profile;

namespace Sms.Api.Controllers;

[Route("v1/staff")]
[Authorize]
public sealed class ProfileController(IProfileService profile) : ApiControllerBase
{
    [HttpGet("profile")]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        FromResult(await profile.GetAsync(ct));
}
