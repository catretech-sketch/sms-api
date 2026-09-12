using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Tenancy;

namespace Sms.Api.Controllers;

public sealed record RazorpaySettingsBody(string? KeyId, string? KeySecret, string? WebhookSecret, string? Mode, bool? Enabled);
public sealed record SaveIntegrationsBody(RazorpaySettingsBody? Razorpay);

[Route("school/integrations")]
[Authorize]
public sealed class SchoolIntegrationsController(ITenantPaymentCredentialService credentials, ITenantContext tenant) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        var status = await credentials.GetStatusAsync(tid, ct);
        return Ok(new
        {
            data = new
            {
                email = new { }, // Email integration backend is a separate future spec — placeholder shape only.
                sms = new { },   // Same for SMS.
                razorpay = new
                {
                    enabled = status.Enabled,
                    key_id = status.KeyId ?? "",
                    mode = status.Mode,
                    status = status.Status,
                    key_secret_set = status.KeySecretSet,
                    webhook_secret_set = status.WebhookSecretSet,
                },
            },
        });
    }

    [HttpPut("")]
    [Authorize(Policy = Policies.SchoolOwnerOnly)]
    public async Task<IActionResult> Save([FromBody] SaveIntegrationsBody body, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        if (body.Razorpay is { } r)
            await credentials.UpsertAsync(tid, new UpsertTenantRazorpayRequest(
                r.KeyId, r.KeySecret, r.WebhookSecret, r.Mode ?? "test", r.Enabled ?? false), ct);
        return await Get(ct);
    }

    [HttpPost("razorpay/verify")]
    [Authorize(Policy = Policies.SchoolOwnerOnly)]
    public async Task<IActionResult> VerifyCredentials(CancellationToken ct)
    {
        if (tenant.TenantId is not { } tid)
            return ForbiddenResult("no tenant context");
        var status = await credentials.GetStatusAsync(tid, ct);
        // A real Razorpay API ping (e.g. GET /v1/orders?count=1 with Basic auth) belongs here once
        // this endpoint needs to distinguish "configured" from "invalid" against the live API —
        // deferred: today it reports "configured" whenever both KeyId and KeySecret are stored,
        // matching GetStatusAsync's existing definition, and never returns "invalid" yet.
        return Ok(new { data = new { status = status.Status } });
    }
}
