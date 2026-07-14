using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Staffing;
using Sms.Modules.Staffing.Contracts;

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
}
