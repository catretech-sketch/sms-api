using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Users;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class InvitationController(IInvitationService invitations) : ApiControllerBase
{
    [HttpGet("invitations")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await invitations.ListAsync(IsSchoolAdmin(), ct));

    [HttpPost("invitations/{id:guid}/resend")]
    public async Task<IActionResult> Resend(Guid id, CancellationToken ct) =>
        FromResult(await invitations.ResendAsync(id, IsSchoolAdmin(), ct));

    [HttpPost("invitations/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) =>
        FromResult(await invitations.RevokeAsync(id, IsSchoolAdmin(), ct));

    private bool IsSchoolAdmin() =>
        User.FindAll("role").Any(c => c.Value is Policies.SchoolAdmin or Policies.SchoolOwner);
}
