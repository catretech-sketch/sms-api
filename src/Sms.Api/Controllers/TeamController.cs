using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class TeamController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("team")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        CursorOk(await tenancy.ListTeamAsync(ct));

    [HttpPost("team")]
    public async Task<IActionResult> Invite([FromBody] InviteTeamRequest req, CancellationToken ct) =>
        FromResult(await tenancy.InviteTeamMemberAsync(req, ct));

    [HttpPatch("team/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamRequest req, CancellationToken ct) =>
        FromResult(await tenancy.UpdateTeamMemberAsync(id, req, ct));

    [HttpPost("team/{id:guid}/documents")]
    public async Task<IActionResult> AddDocument(Guid id, [FromBody] TeamDocumentInput req, CancellationToken ct) =>
        FromResult(await tenancy.AddTeamDocumentAsync(id, req, ct));

    [HttpGet("team/{id:guid}/documents/{docId:guid}")]
    public async Task<IActionResult> GetDocument(Guid id, Guid docId, CancellationToken ct) =>
        FromResult(await tenancy.GetTeamDocumentAsync(id, docId, ct));

    [HttpDelete("team/{id:guid}/documents/{docId:guid}")]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid docId, CancellationToken ct) =>
        FromResult(await tenancy.DeleteTeamDocumentAsync(id, docId, ct));
}
