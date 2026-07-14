using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Auth;

namespace Sms.Api.Controllers;

[Route("v1/me")]
[Authorize]
public sealed class MeSchoolsController(IMeSchoolsService schools) : ApiControllerBase
{
    [HttpGet("schools")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        CursorOk(await schools.ListAsync(ct));

    [HttpGet("schools/fee-summary")]
    public async Task<IActionResult> FeeSummary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        OkData(await schools.FeeSummaryAsync(from, to, ct));

    [HttpPost("schools")]
    public async Task<IActionResult> Create([FromBody] CreateMySchoolRequest req, CancellationToken ct) =>
        FromResult(await schools.CreateAsync(req, ct));

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct) =>
        OkData(await schools.ListPublishedPlansAsync(ct));

    [HttpPost("switch-school")]
    public async Task<IActionResult> SwitchSchool([FromBody] SwitchSchoolRequest req, CancellationToken ct) =>
        FromResult(await schools.SwitchTenantAsync(req.TenantId, ct));
}

public sealed record SwitchSchoolRequest(Guid TenantId);
