using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Academics;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class HousesController(IAcademicsService academics) : ApiControllerBase
{
    [HttpGet("houses")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await academics.ListSchoolHousesAsync(ct));

    public sealed record ReplaceHousesRequest(string[]? Names);

    [HttpPut("houses")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> Replace([FromBody] ReplaceHousesRequest req, CancellationToken ct) =>
        FromResult(await academics.ReplaceSchoolHousesAsync(req.Names ?? [], ct));
}
