using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.DTOs.Users;
using Sms.Application.Services.Users;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class UserController(IUserService users) : ApiControllerBase
{
    [HttpPost("users")]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest req, CancellationToken ct) =>
        FromResult(await users.InviteAsync(req, IsSchoolAdmin(), ct));

    [HttpPost("users/import")]
    public async Task<IActionResult> Import([FromBody] ImportUsersRequest req, CancellationToken ct) =>
        FromResult(await users.ImportAsync(req, IsSchoolAdmin(), ct));

    private bool IsSchoolAdmin() =>
        User.FindAll("role").Any(c => c.Value is Policies.SchoolAdmin or Policies.SchoolOwner);
}
