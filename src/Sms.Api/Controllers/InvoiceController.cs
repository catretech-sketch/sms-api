using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Tenancy;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize(Policy = "platform")]
public sealed class InvoiceController(ITenancyService tenancy) : ApiControllerBase
{
    [HttpGet("invoices")]
    public async Task<IActionResult> List(string? status, [FromQuery(Name = "tenant_id")] Guid? tenantId, CancellationToken ct) =>
        CursorOk(await tenancy.ListInvoicesAsync(status, tenantId, ct));

    [HttpGet("invoices/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.GetInvoiceAsync(id, ct));

    [HttpGet("invoices/{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        var result = await tenancy.GetInvoicePdfAsync(id, ct);
        if (!result.IsSuccess)
            return FromResult(result);
        var (pdf, fileName) = result.Data!;
        return File(pdf, "application/pdf", fileName);
    }

    [HttpPost("invoices/{id:guid}/send")]
    public async Task<IActionResult> SendEmail(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.SendInvoiceEmailAsync(id, ct));

    [HttpPost("invoices/{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.MarkInvoicePaidAsync(id, ct));

    [HttpPost("invoices/{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, CancellationToken ct) =>
        FromResult(await tenancy.RefundInvoiceAsync(id, ct));
}
