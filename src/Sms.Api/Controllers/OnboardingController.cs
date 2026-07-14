using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class OnboardingController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("onboarding")]
    public async Task<IActionResult> List(string? stage, CancellationToken ct) =>
        OkData(await tenancy.ListOnboardingAsync(stage, ct));

    [HttpPost("onboarding")]
    public async Task<IActionResult> Create([FromBody] CreateOnboardingRequest req, CancellationToken ct) =>
        FromResult(await tenancy.CreateOnboardingAsync(req, ct));

    [HttpPost("onboarding/{id:guid}/advance")]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceRequest req, CancellationToken ct) =>
        FromResult(await tenancy.AdvanceOnboardingAsync(id, req, ct));

    [HttpPatch("onboarding/{id:guid}/checklist")]
    public async Task<IActionResult> UpdateChecklist(Guid id, [FromBody] ChecklistRequest req, CancellationToken ct) =>
        FromResult(await tenancy.UpdateOnboardingChecklistAsync(id, req, ct));
}
