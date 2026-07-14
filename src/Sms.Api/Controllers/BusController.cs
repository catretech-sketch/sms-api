using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;
using Sms.Modules.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1/bus")]
[Authorize(Policy = AuthorizationPolicies.TeacherApp)]
public sealed class BusController(IBusService bus) : ApiControllerBase
{
    [HttpGet("assigned")]
    public async Task<IActionResult> GetAssigned(CancellationToken ct) =>
        FromResult(await bus.GetAssignedAsync(ct));

    [HttpGet("{busId:guid}/roster")]
    public async Task<IActionResult> GetRoster(Guid busId, CancellationToken ct) =>
        FromResult(await bus.GetRosterAsync(busId, ct));

    [HttpGet("{busId:guid}/position")]
    public async Task<IActionResult> GetPosition(Guid busId, CancellationToken ct) =>
        FromResult(await bus.GetPositionAsync(busId, ct));

    [HttpPost("{busId:guid}/boarding")]
    public async Task<IActionResult> UpsertBoarding(Guid busId, [FromBody] BusBoardingRequest req, CancellationToken ct) =>
        FromResult(await bus.UpsertBoardingAsync(busId, req, ct));
}
