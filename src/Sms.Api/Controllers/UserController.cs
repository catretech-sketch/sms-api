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
    [HttpGet("users")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await users.ListAsync(IsSchoolAdmin(), ct));

    [HttpPost("users")]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest req, CancellationToken ct) =>
        FromResult(await users.InviteAsync(req, IsSchoolAdmin(), IsSchoolOwner(), ct));

    [HttpPost("users/import")]
    public async Task<IActionResult> Import([FromBody] ImportUsersRequest req, CancellationToken ct) =>
        FromResult(await users.ImportAsync(req, IsSchoolAdmin(), IsSchoolOwner(), ct));

    [HttpPut("users/{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetUserRolesRequest req, CancellationToken ct) =>
        FromResult(await users.SetRolesAsync(id, req, IsSchoolAdmin(), IsSchoolOwner(), ct));

    [HttpGet("users/{id:guid}/permissions")]
    public async Task<IActionResult> GetPermissions(Guid id, CancellationToken ct) =>
        FromResult(await users.GetPermissionsAsync(id, IsSchoolAdmin(), ct));

    [HttpPut("users/{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] SetUserPermissionsRequest req, CancellationToken ct) =>
        FromResult(await users.SetPermissionsAsync(id, req, IsSchoolAdmin(), ct));

    private bool IsSchoolAdmin() =>
        User.FindAll("role").Any(c => c.Value is Policies.SchoolAdmin or Policies.SchoolOwner);

    private bool IsSchoolOwner() =>
        User.FindAll("role").Any(c => c.Value == Policies.SchoolOwner);
}
