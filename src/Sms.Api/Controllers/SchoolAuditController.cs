using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Modules.Tenancy.Data;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1/school")]
[Authorize]
public sealed class SchoolAuditController(AuditRepository audit, ITenantContext tenant) : ApiControllerBase
{
    [HttpGet("audit")]
    public async Task<IActionResult> List(
        string? action, [FromQuery(Name = "actor_id")] Guid? actorId,
        DateTime? from, DateTime? to, string? cursor, CancellationToken ct)
    {
        if (!IsSchoolAdmin())
            return ForbiddenResult("school admin only");
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");

        var (data, nextCursor) = await audit.ListForSchoolAsync(tid, action, actorId, from, to, cursor, pageSize: 50, ct);
        return CursorOk(data, nextCursor);
    }

    private bool IsSchoolAdmin() =>
        User.FindAll("role").Any(c => c.Value is Policies.SchoolAdmin or Policies.SchoolOwner);
}
