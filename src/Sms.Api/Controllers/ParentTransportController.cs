using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

/// Parent/student app transport surface. Strictly scoped: the response is derived from the logged-in
/// account (tenant + linked student), never from any id supplied by the client. A parent sees each
/// linked child; a student login is pinned to that one roster row.
[Route("v1/me")]
[Authorize(Policy = Policies.StudentOrParent)]
public sealed class ParentTransportController(IStudentBusService studentBus) : ApiControllerBase
{
    /// Live bus position for the caller. Includes linked children with no bus so the client can
    /// show "not assigned" rather than an empty screen.
    [HttpGet("children/bus")]
    public async Task<IActionResult> ChildrenBus(CancellationToken ct) =>
        FromResult(await studentBus.GetMyChildrenBusAsync(ct, selfOnly: IsStudentSelf(User)));

    static bool IsStudentSelf(ClaimsPrincipal user)
    {
        var roles = user.FindAll("role").Select(c => c.Value.ToLowerInvariant()).ToHashSet();
        if (roles.Contains("parent") || roles.Contains(Policies.StudentOrParent)) return false;
        return roles.Contains("student");
    }
}
