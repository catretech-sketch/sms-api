using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Sis;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

/// Parent app roster. Children come from ParentStudentLinks for the
/// authenticated parent and tenant — never from a client-supplied id.
[Route("v1/parents")]
[Authorize(Policy = Policies.StudentOrParent)]
public sealed class ParentController(ISisService sis) : ApiControllerBase
{
    [HttpGet("me/children")]
    public async Task<IActionResult> MyChildren(CancellationToken ct) =>
        FromResult(await sis.ListMyChildrenAsync(ct));
}
