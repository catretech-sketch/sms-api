using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class FeeController(IFeeService fees) : ApiControllerBase
{
    [HttpGet("fees/payments")]
    public async Task<IActionResult> ListPayments([FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        var result = await fees.ListPaymentsAsync(studentId, ct);
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!);
    }

    [HttpPost("fees/payments")]
    public async Task<IActionResult> CreatePayment([FromBody] CreateFeePaymentRequest req, CancellationToken ct) =>
        FromResult(await fees.CreatePaymentAsync(req, ct));

    [HttpGet("fees/invoices")]
    public async Task<IActionResult> ListInvoices([FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        var result = await fees.ListInvoicesAsync(studentId, ct);
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!);
    }

    [HttpPost("fees/invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateFeeInvoiceRequest req, CancellationToken ct) =>
        FromResult(await fees.CreateInvoiceAsync(req, ct));

    [HttpPost("fees/invoices/{id:guid}/pay")]
    public async Task<IActionResult> PayInvoice(Guid id, CancellationToken ct) =>
        FromResult(await fees.PayInvoiceAsync(id, ct));
}
