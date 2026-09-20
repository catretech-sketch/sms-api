using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class PlanController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("plans")]
    public async Task<IActionResult> List(string? visibility, string? audience, CancellationToken ct) =>
        OkData(await tenancy.ListPlansAsync(visibility, audience, ct));

    [HttpGet("plans/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.GetPlanAsync(id, ct));

    [HttpPost("plans")]
    public async Task<IActionResult> Create([FromBody] PlanUpsertRequest req, CancellationToken ct) =>
        FromResult(await tenancy.CreatePlanAsync(req, ct));

    [HttpPatch("plans/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PlanUpsertRequest req, CancellationToken ct) =>
        FromResult(await tenancy.UpdatePlanAsync(id, req, ct));

    [HttpPost("plans/{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishPlanRequest req, CancellationToken ct) =>
        FromResult(await tenancy.PublishPlanAsync(id, req, ct));
}
