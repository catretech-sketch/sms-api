using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;
using Sms.Modules.Tenancy.Contracts;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class MePlanUpgradeController(IPlanUpgradeService upgrades) : ApiControllerBase
{
    [HttpPost("me/schools/{tenantId:guid}/upgrade-requests")]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreatePlanUpgradeRequest req, CancellationToken ct) =>
        FromResult(await upgrades.CreateForOwnerAsync(tenantId, req, ct));

    [HttpGet("me/upgrade-requests")]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        CursorOk(await upgrades.ListForOwnerAsync(ct));

    [HttpGet("me/payment-gateway")]
    public IActionResult GatewayStatus() => OkData(upgrades.GetGatewayStatus());

    [HttpPost("me/upgrade-requests/{id:guid}/razorpay-order")]
    public async Task<IActionResult> RazorpayOrder(Guid id, CancellationToken ct) =>
        FromResult(await upgrades.CreateRazorpayOrderAsync(id, ct));

    [HttpPost("me/upgrade-requests/{id:guid}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(
        Guid id, [FromBody] ConfirmPlanUpgradePaymentRequest req, CancellationToken ct) =>
        FromResult(await upgrades.ConfirmPaymentAsync(id, req, ct));
}

[Route("v1/upgrade-requests")]
[Authorize(Policy = "platform")]
public sealed class PlanUpgradeController(IPlanUpgradeService upgrades) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        CursorOk(await upgrades.ListForPlatformAsync(status, ct));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct) =>
        FromResult(await upgrades.ApproveAsync(id, ct));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPlanUpgradeRequest? req, CancellationToken ct) =>
        FromResult(await upgrades.RejectAsync(id, req ?? new RejectPlanUpgradeRequest(null), ct));

    [HttpPost("{id:guid}/razorpay-order")]
    public async Task<IActionResult> RazorpayOrder(Guid id, CancellationToken ct) =>
        FromResult(await upgrades.CreateRazorpayOrderAsync(id, ct));

    [HttpPost("{id:guid}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(
        Guid id, [FromBody] ConfirmPlanUpgradePaymentRequest req, CancellationToken ct) =>
        FromResult(await upgrades.ConfirmPaymentAsync(id, req, ct));
}

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class ClientPlanPaymentController(IPlanUpgradeService upgrades) : ApiControllerBase
{
    [HttpPost("clients/{tenantId:guid}/plan-payments")]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreatePlanPaymentRequest req, CancellationToken ct) =>
        FromResult(await upgrades.CreateForPlatformAsync(tenantId, req, ct));
}

[Route("v1/webhooks")]
[AllowAnonymous]
public sealed class RazorpayWebhookController(IPlanUpgradeService upgrades) : ApiControllerBase
{
    [HttpPost("razorpay")]
    public async Task<IActionResult> Razorpay(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        await upgrades.HandleRazorpayWebhookAsync(body, signature, ct);
        return Ok();
    }
}
