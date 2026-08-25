using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sms.Application.Services.Finance;
using Sms.Application.Services.Sis;
using Sms.Modules.Finance;
using Sms.Shared.Kernel.Authz;
using Sms.Shared.Kernel.Http;
using Sms.Shared.Kernel.Results;

namespace Sms.Api.Controllers;

[Route("v1")]
[Authorize]
public sealed class FeeController(IFeeService fees, ISisService sis) : ApiControllerBase
{
    [HttpGet("fees/payments")]
    public async Task<IActionResult> ListPayments([FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        if (await DenyUnscopedOrUnlinkedAsync(studentId, ct) is { } denied)
            return denied;
        var result = await fees.ListPaymentsAsync(studentId, ct);
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!);
    }

    [HttpPost("fees/payments")]
    public async Task<IActionResult> CreatePayment([FromBody] CreateFeePaymentRequest req, CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await fees.CreatePaymentAsync(req, ct));
    }

    [HttpGet("fees/invoices")]
    public async Task<IActionResult> ListInvoices([FromQuery(Name = "student_id")] Guid? studentId, CancellationToken ct)
    {
        if (await DenyUnscopedOrUnlinkedAsync(studentId, ct) is { } denied)
            return denied;
        var result = await fees.ListInvoicesAsync(studentId, ct);
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!);
    }

    [HttpPost("fees/invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateFeeInvoiceRequest req, CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await fees.CreateInvoiceAsync(req, ct));
    }

    [HttpPost("fees/invoices/{id:guid}/pay")]
    public async Task<IActionResult> PayInvoice(Guid id, [FromBody] PayFeeInvoiceRequest? req, CancellationToken ct)
    {
        var inv = await fees.GetInvoiceAsync(id, ct);
        if (inv is null)
            return NotFoundResult();
        if (!RoleChecks.IsStaff(User) && !await sis.IsLinkedToCallerAsync(inv.StudentId, ct))
            return ForbiddenResult("not your linked student");
        return FromResult(await fees.PayInvoiceAsync(id, req, ct));
    }

    [HttpGet("fees/heads")]
    public async Task<IActionResult> ListHeads(CancellationToken ct)
    {
        var result = await fees.ListHeadsAsync(ct);
        if (result.Error is { } error)
            return StatusCode(result.StatusCode, ErrorEnvelope.From(error));
        return CursorOk(result.Data!);
    }

    [HttpPost("fees/heads")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> CreateHead([FromBody] CreateFeeHeadRequest req, CancellationToken ct) =>
        FromResult(await fees.CreateHeadAsync(req, ct));

    [HttpPatch("fees/heads/{id:guid}")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> UpdateHead(Guid id, [FromBody] UpdateFeeHeadRequest req, CancellationToken ct) =>
        FromResult(await fees.UpdateHeadAsync(id, req, ct));

    [HttpDelete("fees/heads/{id:guid}")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> DeleteHead(Guid id, CancellationToken ct) =>
        FromResult(await fees.DeleteHeadAsync(id, ct));

    [HttpGet("fees/structure")]
    public async Task<IActionResult> GetStructure(CancellationToken ct) =>
        FromResult(await fees.GetStructureAsync(ct));

    [HttpPut("fees/structure")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> UpsertStructure([FromBody] UpsertFeeStructureRequest req, CancellationToken ct) =>
        FromResult(await fees.UpsertStructureAsync(req, ct));

    [HttpPost("fees/invoices/generate")]
    [Authorize(Policy = Policies.Principal)]
    public async Task<IActionResult> GenerateInvoices([FromBody] GenerateFeeInvoicesRequest req, CancellationToken ct) =>
        FromResult(await fees.GenerateInvoicesAsync(req, ct));

    [HttpGet("fees/reports/summary")]
    public async Task<IActionResult> ReportSummary(CancellationToken ct)
    {
        if (!RoleChecks.IsStaff(User))
            return ForbiddenResult("staff only");
        return FromResult(await fees.GetReportSummaryAsync(ct));
    }

    private async Task<IActionResult?> DenyUnscopedOrUnlinkedAsync(Guid? studentId, CancellationToken ct)
    {
        if (RoleChecks.IsStaff(User)) return null;
        if (studentId is not { } sid)
            return ForbiddenResult("student_id is required");
        if (!await sis.IsLinkedToCallerAsync(sid, ct))
            return ForbiddenResult("not your linked student");
        return null;
    }
}
