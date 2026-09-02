using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Staffing;
using Sms.Modules.Staffing.Contracts;
using Sms.Shared.Kernel.Authz;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class StaffController(IStaffingService staffing) : ApiControllerBase
{
    [HttpGet("staff")]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? cat, CancellationToken ct) =>
        FromCursorResult(await staffing.ListStaffAsync(q, cat, ct));

    [HttpGet("staff/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await staffing.GetStaffAsync(id, ct));

    [HttpPost("staff")]
    public async Task<IActionResult> Create([FromBody] CreateStaffRequest req, CancellationToken ct) =>
        FromResult(await staffing.CreateStaffAsync(req, ct));

    [HttpPatch("staff/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffRequest req, CancellationToken ct) =>
        FromResult(await staffing.UpdateStaffAsync(id, req, ct));

    // Admin/principal document management — same nested-resource shape as
    // TeamController's team/{id}/documents, gated to Principal/SchoolAdmin/SchoolOwner.
    [HttpGet("staff/{id:guid}/documents")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> ListDocuments(Guid id, CancellationToken ct) =>
        FromResult(await staffing.ListStaffDocumentsAsync(id, ct));

    [HttpPost("staff/{id:guid}/documents")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> CreateDocument(Guid id, [FromBody] CreateStaffDocumentRequest req, CancellationToken ct) =>
        FromResult(await staffing.CreateStaffDocumentAsync(id, req, ct));

    [HttpPatch("staff/{id:guid}/documents/{docId:guid}")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> UpdateDocument(
        Guid id, Guid docId, [FromBody] UpdateStaffDocumentRequest req, CancellationToken ct) =>
        FromResult(await staffing.UpdateStaffDocumentAsync(id, docId, req, ct));

    [HttpDelete("staff/{id:guid}/documents/{docId:guid}")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid docId, CancellationToken ct) =>
        FromResult(await staffing.DeleteStaffDocumentAsync(id, docId, ct));
}
