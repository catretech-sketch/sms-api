using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Transport;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

/// Parent app transport surface. Strictly scoped: the response is derived from the logged-in
/// parent's own account (tenant + linked student), never from any id supplied by the client, so a
/// parent can only ever see their own child's bus — even across schools that share roads/stops/bus numbers.
[Route("v1/me")]
[Authorize(Policy = Policies.StudentOrParent)]
public sealed class ParentTransportController(IStudentBusService studentBus) : ApiControllerBase
{
    /// Live bus position for the caller's child (or children). Empty when the account is not linked
    /// to a student or the child has no bus assigned.
    [HttpGet("children/bus")]
    public async Task<IActionResult> ChildrenBus(CancellationToken ct) =>
        FromResult(await studentBus.GetMyChildrenBusAsync(ct));
}
