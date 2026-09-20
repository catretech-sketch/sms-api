using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class AuditController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("audit")]
    public async Task<IActionResult> List(string? kind, [FromQuery(Name = "actor_id")] Guid? actorId,
        [FromQuery(Name = "tenant_id")] Guid? tenantId, CancellationToken ct) =>
        CursorOk(await tenancy.ListAuditAsync(kind, actorId, tenantId, ct));
}
