using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class SubscriptionController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("subscriptions")]
    public async Task<IActionResult> List(string? status, [FromQuery(Name = "tenant_id")] Guid? tenantId, CancellationToken ct) =>
        CursorOk(await tenancy.ListSubscriptionsAsync(status, tenantId, ct));

    [HttpGet("subscriptions/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.GetSubscriptionAsync(id, ct));

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest req, CancellationToken ct) =>
        FromResult(await tenancy.CreateSubscriptionAsync(req, ct));
}
